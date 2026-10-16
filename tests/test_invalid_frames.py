from pathlib import Path
import struct,ctypes,subprocess
p=Path(__file__).resolve().parents[1];stage=p/'tests/build/invalid-frames';stage.mkdir(parents=True,exist_ok=True)
kernel=ctypes.WinDLL('kernel32');kernel.GetTickCount64.restype=ctypes.c_uint64
path=stage/'frames.bin';probe=p/'virtual-camera/build/CameraProbe.exe';dll=p/'virtual-camera/build/PCamCamera.dll'
for label,magic,slot,sequence,size in [('invalid magic',0,0,0,7372864),('invalid slot',0x5043414D,99,0,7372864),('incomplete writer',0x5043414D,0,1,7372864),('truncated mapping',0x5043414D,0,0,64)]:
 with path.open('wb') as stream:
  stream.write(struct.pack('<iiIIQ40s',sequence,slot,magic,0,kernel.GetTickCount64(),b''));stream.truncate(size)
 result=subprocess.run([str(probe),'--direct',str(dll),str(path)],capture_output=True,text=True,timeout=20)
 assert result.returncode==0 and 'nonblack=0' in result.stdout,(label,result.stdout,result.stderr)
 print('PASS:',label,'returns black without a crash',flush=True)
