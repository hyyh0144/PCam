// PCam 品牌提示，保留 v2 二维码协议兼容性。
package cn.phonecam;
import java.net.URI;
import java.net.URLDecoder;
import java.util.*;
public final class PairingInfo {
    public final String[] hosts;
    public final int port;
    public final String code, id, name;
    private PairingInfo(String[] hosts,int port,String code,String id,String name) {
        this.hosts=hosts; this.port=port; this.code=code; this.id=id; this.name=name;
    }
    public static PairingInfo parse(String text) {
        try {
            if(text==null || text.length()>2048) throw new IllegalArgumentException();
            URI uri=new URI(text);
            if(!"phonecam".equals(uri.getScheme()) || !"pair".equals(uri.getRawAuthority()) || !"".equals(uri.getRawPath()) || uri.getFragment()!=null) throw new IllegalArgumentException();
            Map<String,String> values=new HashMap<>();
            for(String item:uri.getRawQuery().split("&")) {
                String[] pair=item.split("=",2);
                if(pair.length!=2 || values.put(pair[0],URLDecoder.decode(pair[1],"UTF-8"))!=null) throw new IllegalArgumentException();
            }
            if(values.size()!=6 || !"2".equals(values.get("v"))) throw new IllegalArgumentException();
            String[] hosts=values.get("hosts").split(",",-1);
            if(hosts.length<1 || hosts.length>8) throw new IllegalArgumentException();
            for(String host:hosts) {
                String[] octets=host.split("\\.",-1);
                if(octets.length!=4) throw new IllegalArgumentException();
                for(String octet:octets) if(!octet.matches("0|[1-9][0-9]{0,2}") || Integer.parseInt(octet)>255) throw new IllegalArgumentException();
                int first=Integer.parseInt(octets[0]);
                if(first==0 || first>=224 || "255".equals(octets[3])) throw new IllegalArgumentException();
            }
            String portText=values.get("port"), code=values.get("code"), id=values.get("id"), name=values.get("name");
            if(!portText.matches("[0-9]{1,5}")) throw new IllegalArgumentException();
            int port=Integer.parseInt(portText);
            if(port<1 || port>65535 || !code.matches("[0-9A-F]{16}") || !UUID.fromString(id).toString().equals(id)) throw new IllegalArgumentException();
            if(name==null || name.length()>80 || name.matches("(?s).*\\p{Cntrl}.*")) throw new IllegalArgumentException();
            return new PairingInfo(hosts,port,code,id,name.isEmpty()?"电脑":name);
        } catch(Exception e) { throw new IllegalArgumentException("请扫描 PCam 电脑端的二维码"); }
    }
}
