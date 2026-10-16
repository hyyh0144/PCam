using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace PhoneCam;

public static class CameraInstallation
{
    public const string Clsid = "{E596E156-A65E-489A-A4FB-F2F4B0D23E01}";
    // Use the project version so component updates cannot retain an obsolete installation path.
    public static readonly string Version = typeof(CameraInstallation).Assembly.GetName().Version!.ToString(3);
    static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier SystemAccount = new(WellKnownSidType.LocalSystemSid, null);
    static readonly Lazy<byte[]> EmbeddedDll = new(ReadPayload);
    static readonly Lazy<string> PayloadHash = new(() => Convert.ToHexString(SHA256.HashData(EmbeddedDll.Value)));
    public static string Sid => WindowsIdentity.GetCurrent().User!.Value;

    public static string FramesDirectory(string sid)
    {
        // SID is command-line input to an elevated helper: never accept it as a path fragment.
        var identity = new SecurityIdentifier(sid);
        if (!identity.IsAccountSid() || identity.Value != sid) throw new ArgumentException("用户标识无效");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCam", "Frames", identity.Value);
    }
    public static string FrameFile => Path.Combine(FramesDirectory(Sid), "frames.bin");
    static byte[] ReadPayload()
    {
        using var stream = typeof(CameraInstallation).Assembly.GetManifestResourceStream("PCam.Camera.dll")
            ?? throw new InvalidOperationException("缺少虚拟摄像头组件");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
    public static string InstalledPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "PCam", "VirtualCamera", Version, PayloadHash.Value[..12], "PCamCamera.dll");

    public static void RejectReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                // Path.Exists follows links and may report false for a dangling link.
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("安装路径含目录联接或符号链接，请检查 PCam 目录");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public static bool IsInstalled()
    {
        try
        {
            RejectReparsePoints(InstalledPath);
            RejectReparsePoints(FramesDirectory(Sid));
            if(!HasSafeFrameDirectory())return false;
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + Clsid + @"\InprocServer32");
            if (!string.Equals(key?.GetValue(null) as string, InstalledPath, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(FramesDirectory(Sid))) return false;
            // Check the actual DLL too; directory name/registration alone do not prove integrity.
            using var file = File.OpenRead(InstalledPath);
            return Convert.ToHexString(SHA256.HashData(file)) == PayloadHash.Value;
        }
        catch { return false; }
    }
    static bool HasSafeFrameDirectory()
    {
        var security=new DirectoryInfo(FramesDirectory(Sid)).GetAccessControl(AccessControlSections.Access|AccessControlSections.Owner);
        if(!security.AreAccessRulesProtected || !Administrators.Equals(security.GetOwner(typeof(SecurityIdentifier))))return false;
        const FileSystemRights forbidden=FileSystemRights.Delete|FileSystemRights.WriteAttributes|FileSystemRights.WriteExtendedAttributes|
            FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership|FileSystemRights.CreateDirectories;
        foreach(FileSystemAccessRule rule in security.GetAccessRules(true,false,typeof(SecurityIdentifier)))
            if(rule.IdentityReference.Value==Sid && rule.AccessControlType==AccessControlType.Allow &&
                (rule.PropagationFlags&PropagationFlags.InheritOnly)==0 && (rule.FileSystemRights&forbidden)!=0)return false;
        return true;
    }
    static DirectorySecurity DirectoryRules(SecurityIdentifier? user = null)
    {
        var rules = new DirectorySecurity();
        rules.SetAccessRuleProtection(true, false);
        rules.SetOwner(Administrators);
        void Add(SecurityIdentifier identity, FileSystemRights rights) => rules.AddAccessRule(new(identity, rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        Add(SystemAccount, FileSystemRights.FullControl);
        Add(Administrators, FileSystemRights.FullControl);
        if (user == null) Add(new(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute);
        else
        {
            // The user can create frame files, but cannot replace/reparse the SID directory itself.
            rules.AddAccessRule(new(user,FileSystemRights.ReadAndExecute|FileSystemRights.CreateFiles,
                InheritanceFlags.None,PropagationFlags.None,AccessControlType.Allow));
            rules.AddAccessRule(new(user,FileSystemRights.Modify,
                InheritanceFlags.ObjectInherit,PropagationFlags.InheritOnly,AccessControlType.Allow));
            Add(new(WellKnownSidType.LocalServiceSid, null), FileSystemRights.ReadAndExecute);
        }
        return rules;
    }
    static void SecureDirectory(string path, DirectorySecurity rules)
    {
        RejectReparsePoints(path);
        var directory = new DirectoryInfo(path);
        // Create with its restrictive ACL from the outset. Refuse pre-planted user-owned folders.
        if (!directory.Exists) directory.Create(rules);
        InstallDirectorySecurity.Apply(path,rules);

    }
    static void SecureTree(string parent, params string[] names)
    {
        foreach (string name in names)
        {
            parent = Path.Combine(parent, name);
            SecureDirectory(parent, DirectoryRules());
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Register();
    public static void Install(string sid)
    {
        string frames = FramesDirectory(sid);
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("注册摄像头组件需要管理员权限");
        SecureTree(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PCam", "VirtualCamera", Version, PayloadHash.Value[..12]);
        SecureTree(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCam", "Frames");
        SecureDirectory(frames, DirectoryRules(new(sid)));
        RejectReparsePoints(InstalledPath);
        if (!File.Exists(InstalledPath))
        {
            using var file = new FileStream(InstalledPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(EmbeddedDll.Value);
        }
        using (var file = File.OpenRead(InstalledPath))
            if (Convert.ToHexString(SHA256.HashData(file)) != PayloadHash.Value) throw new IOException("摄像头组件校验失败");
        nint dll = NativeLibrary.Load(InstalledPath);
        try { Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<Register>(NativeLibrary.GetExport(dll, "PcRegister"))()); }
        finally { NativeLibrary.Free(dll); }
    }
    public static async Task EnsureInstalled()
    {
        if (IsInstalled()) return;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--install-camera");
        start.ArgumentList.Add(Sid);
        using var process = Process.Start(start) ?? throw new IOException("摄像头安装未启动");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !IsInstalled()) throw new IOException("虚拟摄像头组件安装未完成，请重新打开 PCam 并允许安装");
    }
}
