/*
Copyright (C) 2008  David Wolinsky <davidiw@ufl.edu>, University of Florida

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

using Brunet;
using Brunet.DistributedServices;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Ipop {
  public class RpcAddressResolverAndDNS: IAddressResolver, IRpcHandler {
    protected StructuredNode _node;
    protected RpcManager _rpc;
    protected readonly object _sync = new object();
    protected volatile Hashtable dns_a; /**< Maps names to IP Addresses */
    protected volatile Hashtable dns_ptr; /**< Maps IP Addresses to names */
    protected volatile Hashtable ip_addr; /**< Maps IP Addresses to Brunet Addresses */
    protected volatile Hashtable addr_ip; /**< Maps Brunet Addresses to IP Addresses */
    protected Object _sync;

    public RpcAddressResolverAndDNS(StructuredNode node) {
      _node = node;
      _rpc = RpcManager.GetInstance(node);
      dns_a = new Hashtable();
      dns_ptr = new Hashtable();
      ip_addr = new Hashtable();
      addr_ip = new Hashtable();
      _sync = new Object();

      _rpc.AddHandler("RpcIpopNode", this);
    }

    /**
     * Returns the Brunet address given an IP
     * @param IP IP Address to look up
     * @return null if no IP exists or the Brunet.Address
     */
    public Address GetAddress(String IP) {
      return (Address) ip_addr[IP];
    }

    public IPPacket LookUp(IPPacket req_ipp) {
      UDPPacket req_udpp = new UDPPacket(req_ipp.Payload);
      DNSPacket dnspacket = new DNSPacket(req_udpp.Payload);
      ICopyable rdnspacket = null;
      try {
        string qname_response = String.Empty;
        string qname = dnspacket.Questions[0].QNAME;
        if(dnspacket.Questions[0].QTYPE == DNSPacket.TYPES.A) {
          qname_response = (string) dns_a[qname];
          if(qname_response == null) {
            throw new Exception("Dht does not contain a record for " + qname);
          }
        }
        else if(dnspacket.Questions[0].QTYPE == DNSPacket.TYPES.PTR) {
          qname_response = (string) dns_ptr[qname];
          if(qname_response == null) {
            throw new Exception("DNS PTR does not contain a record for " + qname);
          }
        }
        DNSPacket.Response response = new DNSPacket.Response(qname, dnspacket.Questions[0].QTYPE,
                                dnspacket.Questions[0].QCLASS, 1800, qname_response);
        DNSPacket res_packet = new DNSPacket(dnspacket.ID, false, dnspacket.OPCODE, true,
                                         dnspacket.Questions, new DNSPacket.Response[] {response});
        rdnspacket = res_packet.ICPacket;
      }
      catch(Exception e) {
        ProtocolLog.WriteIf(IpopLog.DNS, e.Message);
        rdnspacket = DNSPacket.BuildFailedReplyPacket(dnspacket);
      }
      UDPPacket res_udpp = new UDPPacket(req_udpp.DestinationPort,
                                         req_udpp.SourcePort, rdnspacket);
      IPPacket res_ipp = new IPPacket((byte) IPPacket.Protocols.UDP,
                 req_ipp.DestinationIP, req_ipp.SourceIP, res_udpp.ICPacket);
      return res_ipp;
    }

    public void HandleRpc(ISender caller, String method, IList arguments, object request_state) {
      try {
        ReqrepManager.ReplyState _rs = (ReqrepManager.ReplyState) caller;
        UnicastSender _us = (UnicastSender) _rs.ReturnPath;
        IPEndPoint _ep = (IPEndPoint) _us.EndPoint;
        String ip = _ep.Address.ToString();
        if(ip != "127.0.0.1") {
          throw new Exception();
        }
      }
      catch {
        Object rs = new InvalidOperationException("Not calling from local BrunetRpc locally!");
        _rpc.SendResult(request_state, rs);
        return;
      }

      Object result = new InvalidOperationException("Invalid method");
      try {
        if(method.Equals("RegisterMapping")) {
          String name = (String) arguments[0];
          Address addr = AddressParser.Parse((String) arguments[1]);
          result = RegisterMapping(name, addr);
        }
        else if(method.Equals("UnregisterMapping")) {
          String name = (String) arguments[0];
          result = UnregisterMapping(name);
        }
      }
      catch {
        result = new InvalidOperationException("Bad parameters.");
      }
      _rpc.SendResult(request_state, result);
    }

    protected bool RegisterMapping(String name, Address addr) {
      if(dns_a.Contains(name)) {
        throw new Exception("Name ({0}) already exists.", name);
      }
      if(addr_ip.Contains(addr)) {
        throw new Exception("Address ({0}) already exists.", addr);
      }
      return false;
    }

    protected bool UnregisterMapping(String name) {
      if(dns_a.Contains(name)) {
        throw new Exception("Name ({0}) doesnot exists", name);
      }
      lock(_sync) {
        String ip = dns_a[name];
        dns_a.Remove(name);
        try {
          dns_ptr.Remove(ip);
        } catch {}
        try {
          Address addr = ip_addr[ip];
          ip_addr.Remove(ip);
          addr_ip.Remove(addr);
        }
      }
      return true;
    }
  }
}