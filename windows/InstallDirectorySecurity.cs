using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PhoneCam;

// Installation runs elevated. Inspect and change the same directory HANDLE;
// path-based check-then-set operations could follow a link substituted in between.
internal static class InstallDirectorySecurity
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeFileHandle CreateFile(string path,uint access,uint share,nint security,uint disposition,uint flags,nint template);
    [StructLayout(LayoutKind.Sequential)]struct AttributeTag{public uint Attributes,Tag;}
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool GetFileInformationByHandleEx(SafeFileHandle file,int kind,out AttributeTag info,uint size);
    [DllImport("advapi32.dll")]
    static extern uint GetSecurityInfo(SafeFileHandle handle,uint type,uint information,out nint owner,out nint group,out nint dacl,out nint sacl,out nint descriptor);
    [DllImport("advapi32.dll")]
    static extern uint SetSecurityInfo(SafeFileHandle handle,uint type,uint information,nint owner,nint group,nint dacl,nint sacl);
    [DllImport("advapi32.dll",SetLastError=true)]
    static extern bool GetSecurityDescriptorOwner(nint descriptor,out nint owner,out bool defaulted);
    [DllImport("advapi32.dll",SetLastError=true)]
    static extern bool GetSecurityDescriptorDacl(nint descriptor,out bool present,out nint dacl,out bool defaulted);
    [DllImport("kernel32.dll")]static extern nint LocalFree(nint memory);
    static void Check(bool result){if(!result)throw new Win32Exception(Marshal.GetLastWin32Error());}
    static void Check(uint result){if(result!=0)throw new Win32Exception((int)result);}

    public static void Apply(string path,DirectorySecurity security)
    {
        const uint ReadControl=0x20000,WriteDacl=0x40000,WriteOwner=0x80000;
        const uint OpenExisting=3,OpenReparsePoint=0x00200000,BackupSemantics=0x02000000;
        const uint FileObject=1,OwnerInformation=1,DaclInformation=4,ProtectedDacl=0x80000000;
        using var handle=CreateFile(path,ReadControl|WriteDacl|WriteOwner,3,0,OpenExisting,OpenReparsePoint|BackupSemantics,0);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        Check(GetFileInformationByHandleEx(handle,9,out var attributes,8));
        if((attributes.Attributes&(uint)FileAttributes.ReparsePoint)!=0)throw new IOException("安装目录不能是联接或符号链接");
        Check(GetSecurityInfo(handle,FileObject,OwnerInformation,out var owner,out _,out _,out _,out var descriptor));
        try
        {
            var identity=new SecurityIdentifier(owner);
            if(!identity.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !identity.IsWellKnown(WellKnownSidType.LocalSystemSid))
                throw new IOException("PCam 安装目录所有者异常，请检查目录后重试");
        }
        finally{LocalFree(descriptor);}
        byte[] bytes=security.GetSecurityDescriptorBinaryForm();
        var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try
        {
            nint address=pinned.AddrOfPinnedObject();
            Check(GetSecurityDescriptorOwner(address,out var newOwner,out _));
            Check(GetSecurityDescriptorDacl(address,out bool present,out var dacl,out _));
            if(!present || dacl==0)throw new IOException("拒绝空目录访问控制表");
            Check(SetSecurityInfo(handle,FileObject,OwnerInformation|DaclInformation|ProtectedDacl,newOwner,0,dacl,0));
        }
        finally{pinned.Free();}
    }
}
