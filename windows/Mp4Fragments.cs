using System.Buffers.Binary;
using System.IO;
using System.Text;
namespace PhoneCam;

// A recoverable prefix ends at a complete moof/mdat pair, never at a partially written box.
sealed class Mp4Fragments
{
    long position;
    bool header,fragment;
    double pendingSeconds;
    readonly Dictionary<uint,uint> scales=new(),defaults=new();
    readonly Dictionary<uint,ulong> ends=new();
    public long CompleteLength {get;private set;}
    public int Fragments {get;private set;}
    public double CompleteSeconds {get;private set;}
    static uint U32(byte[] b,int o)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o,4));
    static ulong U64(byte[] b,int o)=>BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(o,8));
    static void Need(int size,int minimum){if(size<minimum)throw new IOException("录像元数据被截断");}
    static IEnumerable<(int Offset,int Size,string Type)> Boxes(byte[] b,int start,int end){
        int p=start;
        while(p<end){if(end-p<8)throw new IOException("录像元数据尾部被截断");uint n=U32(b,p);if(n<8||n>(uint)(end-p))throw new IOException("录像元数据结构无效");yield return(p,(int)n,Encoding.ASCII.GetString(b,p+4,4));p+=(int)n;}
    }
    static byte[] Metadata(FileStream file,long offset,ulong size){
        if(size>2*1024*1024)throw new IOException("录像元数据超出限制");byte[] b=new byte[(int)size];file.Position=offset;file.ReadExactly(b);return b;
    }
    void ReadMovie(byte[] b){
        foreach(var box in Boxes(b,8,b.Length)){
            if(box.Type=="trak"){
                uint id=0,scale=0;
                foreach(var child in Boxes(b,box.Offset+8,box.Offset+box.Size)){
                    if(child.Type=="tkhd"){Need(child.Size,9);Need(child.Size,b[child.Offset+8]==1?32:24);id=U32(b,child.Offset+(b[child.Offset+8]==1?28:20));}
                    if(child.Type=="mdia")foreach(var item in Boxes(b,child.Offset+8,child.Offset+child.Size))if(item.Type=="mdhd"){Need(item.Size,9);Need(item.Size,b[item.Offset+8]==1?32:24);scale=U32(b,item.Offset+(b[item.Offset+8]==1?28:20));}
                }
                if(id!=0&&scale!=0)scales[id]=scale;
            }
            if(box.Type=="mvex")foreach(var child in Boxes(b,box.Offset+8,box.Offset+box.Size))if(child.Type=="trex"){Need(child.Size,24);defaults[U32(b,child.Offset+12)]=U32(b,child.Offset+20);}
        }
    }
    double FragmentSeconds(byte[] b){
        double seconds=0;
        foreach(var traf in Boxes(b,8,b.Length))if(traf.Type=="traf"){
            uint id=0,duration=0;ulong time=0;
            var children=Boxes(b,traf.Offset+8,traf.Offset+traf.Size).ToArray();
            foreach(var box in children){int p=box.Offset;
                if(box.Type=="tfhd"){Need(box.Size,16);uint flags=U32(b,p+8)&0xffffff;id=U32(b,p+12);time=ends.GetValueOrDefault(id);duration=defaults.GetValueOrDefault(id);int o=p+16;if((flags&1)!=0)o+=8;if((flags&2)!=0)o+=4;if((flags&8)!=0){Need(box.Size,o-p+4);duration=U32(b,o);o+=4;}if((flags&0x10)!=0)o+=4;if((flags&0x20)!=0)o+=4;Need(box.Size,o-p);}
                if(box.Type=="tfdt"){Need(box.Size,9);Need(box.Size,b[p+8]==1?20:16);time=b[p+8]==1?U64(b,p+12):U32(b,p+12);}
            }
            if(!scales.TryGetValue(id,out uint scale))throw new IOException("录像片段轨道无效");
            foreach(var box in children)if(box.Type=="trun"){
                int p=box.Offset;Need(box.Size,16);uint flags=U32(b,p+8)&0xffffff,count=U32(b,p+12);if(count>200000)throw new IOException("录像片段样本过多");int o=p+16;if((flags&1)!=0)o+=4;if((flags&4)!=0)o+=4;
                // Validate the entire table before reading any sample: a malformed trun must not borrow sibling bytes.
                int fields=((flags&0x100)!=0?1:0)+((flags&0x200)!=0?1:0)+((flags&0x400)!=0?1:0)+((flags&0x800)!=0?1:0);
                Need(box.Size,checked(o-p+(int)count*fields*4));
                for(uint i=0;i<count;i++){
                    uint step=duration;if((flags&0x100)!=0){step=U32(b,o);o+=4;}if((flags&0x200)!=0)o+=4;if((flags&0x400)!=0)o+=4;if((flags&0x800)!=0)o+=4;
                    if(o>p+box.Size||time>ulong.MaxValue-step)throw new IOException("录像片段时间无效");time+=step;
                }
                seconds=Math.Max(seconds,(double)time/scale);ends[id]=time;
            }
        }
        return seconds;
    }
    public long Update(string path){
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        Span<byte> bytes=stackalloc byte[16];long length=file.Length;
        while(position<=length-8){
            file.Position=position;file.ReadExactly(bytes[..8]);ulong size=BinaryPrimitives.ReadUInt32BigEndian(bytes);int headerSize=8;
            if(size==1){if(position>length-16)break;file.ReadExactly(bytes[8..16]);size=BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);headerSize=16;}
            if(size==0||size<(ulong)headerSize||size>(ulong)(length-position))break;
            string kind=Encoding.ASCII.GetString(bytes[4..8]);
            if(position==0&&kind!="ftyp")throw new IOException("临时录像格式无效");
            try{
                if(kind=="moov"){if(header||fragment||headerSize!=8)throw new IOException("临时录像文件头重复或不受支持");ReadMovie(Metadata(file,position,size));header=true;}
                if(kind=="moof"){if(!header||fragment||headerSize!=8)throw new IOException("临时录像片段顺序无效");pendingSeconds=FragmentSeconds(Metadata(file,position,size));fragment=true;}
            }catch(ArgumentOutOfRangeException e){throw new IOException("录像元数据被截断",e);}
            position+=(long)size;
            if(kind=="mdat"&&fragment){CompleteLength=position;CompleteSeconds=Math.Max(CompleteSeconds,pendingSeconds);Fragments++;fragment=false;}
        }
        return CompleteLength;
    }
    internal static (long Offset,int Count,uint Scale) DurationField(FileStream output){
        output.Position=0;Span<byte> header=stackalloc byte[8];
        while(output.Position<=output.Length-8){long offset=output.Position;output.ReadExactly(header);uint size=BinaryPrimitives.ReadUInt32BigEndian(header);if(size<8||size>output.Length-offset)throw new IOException("正式录像文件头无效");
            if(Encoding.ASCII.GetString(header[4..])=="moov"){
                byte[] b=Metadata(output,offset,size);
                foreach(var box in Boxes(b,8,b.Length))if(box.Type=="mvhd"){
                    int p=box.Offset;Need(box.Size,9);if(b[p+8]>1)throw new IOException("录像时长版本无效");bool wide=b[p+8]==1;Need(box.Size,wide?40:28);uint scale=U32(b,p+(wide?28:20));if(scale==0)throw new IOException("录像时长无效");
                    return(offset+p+(wide?32:24),wide?8:4,scale);
                }
                throw new IOException("录像缺少时长信息");
            }
            output.Position=offset+size;
        }
        throw new IOException("录像缺少文件头");
    }
    internal static void SetDuration(FileStream output,double seconds){
        var field=DurationField(output);double valueTicks=Math.Ceiling(seconds*field.Scale);
        if(!double.IsFinite(seconds)||seconds<0||!double.IsFinite(valueTicks)||valueTicks>=ulong.MaxValue||(field.Count==4&&valueTicks>uint.MaxValue))throw new IOException("录像时长超出范围");
        Span<byte> value=stackalloc byte[8];if(field.Count==8)BinaryPrimitives.WriteUInt64BigEndian(value,(ulong)valueTicks);else BinaryPrimitives.WriteUInt32BigEndian(value,(uint)valueTicks);
        output.Position=field.Offset;output.Write(value[..field.Count]);
    }
}



