from pathlib import Path
import ctypes,os,subprocess,csv
p=Path(__file__).resolve().parents[1];stage=p/'tests/build/audit-security';stage.mkdir(parents=True,exist_ok=True)
dll=ctypes.WinDLL(str(p/'virtual-camera/build/PCamCamera.dll'));dll.PcStart.argtypes=[ctypes.c_wchar_p,ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_void_p)]
original=stage/'original.bin';link=stage/'hardlink.bin';original.write_bytes(b'untouched marker')
if link.exists():link.unlink()
os.link(original,link);context=ctypes.c_void_p();hr=dll.PcStart(str(link),'audit-hardlink-boundary',ctypes.byref(context))
assert hr & 0xffffffff==0x80070005,hex(hr&0xffffffff);assert original.read_bytes()==b'untouched marker';link.unlink()
print('PASS hard-linked frame file rejected without altering target')
identity=list(csv.reader([subprocess.check_output(['whoami','/user','/fo','csv','/nh'],text=True).strip()]))[0][1]
file=Path(os.environ['ProgramData'])/'PCam'/'Frames'/identity/'frames.bin' 
try:
 with file.open('r+b'):pass
 raise AssertionError('second writer unexpectedly accepted')
except PermissionError:print('PASS live frame mapping rejects another writer/truncation')

kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p]
kernel.CreateFileW.restype=ctypes.c_void_p
handle=kernel.CreateFileW(str(file.parent),0x100,7,None,3,0x02000000,None)
if handle!=ctypes.c_void_p(-1).value:
 kernel.CloseHandle(ctypes.c_void_p(handle));raise AssertionError('directory WriteAttributes unexpectedly allowed')
assert ctypes.get_last_error()==5,ctypes.get_last_error()
print('PASS user cannot rewrite frame directory attributes/reparse metadata')
