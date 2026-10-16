from pathlib import Path
import struct,ctypes,subprocess,time,threading
p=Path(__file__).resolve().parents[1];stage=p/'tests/build/direct-camera';stage.mkdir(parents=True,exist_ok=True)
kernel=ctypes.WinDLL('kernel32');kernel.GetTickCount64.restype=ctypes.c_uint64
w,h=1280,720;pixels=bytearray(w*h*4)
for y in range(h):
 for x in range(w):
  color=(0,0,255,255) if x<w//2 and y<h//2 else (0,255,0,255) if x>=w//2 and y<h//2 else (255,0,0,255) if x<w//2 else (0,255,255,255)
  i=(y*w+x)*4;pixels[i:i+4]=bytes(color)
path=stage/'frames.bin';path.write_bytes(struct.pack('<iiIIQ40s',0,0,0x5043414D,0,kernel.GetTickCount64(),b'')+pixels+pixels)
alive=True
def heartbeat():
 with path.open('r+b',buffering=0) as stream:
  while alive:
   stream.seek(16);stream.write(struct.pack('<Q',kernel.GetTickCount64()));time.sleep(.1)
thread=threading.Thread(target=heartbeat,daemon=True);thread.start()
probe=p/'virtual-camera/build/CameraProbe.exe';dll=p/'virtual-camera/build/PCamCamera.dll'
result=subprocess.run([str(probe),'--direct',str(dll),str(path),str(stage/'frame.bgra')],capture_output=True,text=True,timeout=30)
print(result.stdout,result.stderr,flush=True);assert result.returncode==0
frame=(stage/'frame.bgra').read_bytes();assert len(frame)==w*h*4
for x,y,expected in [(100,100,(0,0,255)),(1100,100,(0,255,0)),(100,600,(255,0,0)),(1100,600,(0,255,255))]:
 i=(y*w+x)*4;assert tuple(frame[i:i+3])==expected,(x,y,tuple(frame[i:i+3]))
print('PASS: independent quadrant pixels through COM media source and MF Source Reader',flush=True)
alive=False;thread.join()
time.sleep(2.1)
result=subprocess.run([str(probe),'--direct',str(dll),str(path)],capture_output=True,text=True,timeout=30)
print(result.stdout,result.stderr,flush=True);assert result.returncode==0 and 'nonblack=0' in result.stdout
print('PASS: stale producer data becomes black',flush=True)
