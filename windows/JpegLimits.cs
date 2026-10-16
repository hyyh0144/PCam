namespace PhoneCam;

// Compressed byte limits alone cannot bound JPEG decode memory. Read the SOF
// dimensions before WIC allocates the decoded bitmap (16 MP / 8192 per side).
public static class JpegLimits
{
    public const int MaxDimension=8192;
    public const long MaxPixels=16_777_216;
    public static bool IsSafe(ReadOnlySpan<byte> data)
    {
        if(data.Length<4 || data.Length>Receiver.MaxFrame || data[0]!=0xff || data[1]!=0xd8)return false;
        int offset=2;
        while(offset<data.Length)
        {
            if(data[offset++]!=0xff)return false;
            while(offset<data.Length && data[offset]==0xff)offset++;
            if(offset>=data.Length)return false;
            byte marker=data[offset++];
            if(marker==0xda || marker==0xd9 || marker==0)return false; // Need dimensions before scan data.
            if(marker==0x01 || marker is >=0xd0 and <=0xd7)continue;
            if(offset+2>data.Length)return false;
            int length=(data[offset]<<8)|data[offset+1];
            if(length<2 || length>data.Length-offset)return false;
            bool startOfFrame=marker is >=0xc0 and <=0xcf && marker!=0xc4 && marker!=0xc8 && marker!=0xcc;
            if(startOfFrame)
            {
                if(length<8)return false;
                int height=(data[offset+3]<<8)|data[offset+4];
                int width=(data[offset+5]<<8)|data[offset+6];
                int components=data[offset+7];
                return components is >=1 and <=4 && length>=8+3*components &&
                    width>0 && height>0 && width<=MaxDimension && height<=MaxDimension && (long)width*height<=MaxPixels;
            }
            offset+=length;
        }
        return false;
    }
}
