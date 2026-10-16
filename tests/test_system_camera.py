# Run PCam --virtual-camera-test <new-stage-directory> first.
from pathlib import Path
import subprocess,time,json
p=Path(__file__).resolve().parents[1];stage=Path(__import__('sys').argv[1]) if len(__import__('sys').argv)>1 else p/'tests/build/virtual-camera-release'
print('Waiting for Windows registration consent and real camera startup',flush=True)
deadline=time.monotonic()+600
while not (stage/'ready').exists():
 log=(stage/'publisher.log').read_text(encoding='utf-8-sig') if (stage/'publisher.log').exists() else ''
 if '未启动' in log or '不可用' in log:raise RuntimeError(log)
 if time.monotonic()>deadline:raise TimeoutError('Windows camera registration consent is still pending')
 time.sleep(.5)
print('Camera registered; starting separate system-camera consumer',flush=True)
probe=p/'virtual-camera/build/CameraProbe.exe';raw=stage/'system-frame.bgra'
r=subprocess.run([str(probe),str(raw)],capture_output=True,text=True,timeout=30)
(stage/'capture.log').write_text(r.stdout+'\n'+r.stderr,encoding='utf-8');print(r.stdout,r.stderr,flush=True);assert r.returncode==0
frame=raw.read_bytes();assert len(frame)==1280*720*4
samples=[]
for x,y,expected in [(240,120,(0,0,255)),(1040,120,(0,255,0)),(240,600,(255,0,0)),(1040,600,(0,255,255)),(80,360,(0,0,0))]:
 i=(y*1280+x)*4;actual=tuple(frame[i:i+3]);samples.append({'xy':[x,y],'bgr':actual});assert max(abs(a-b) for a,b in zip(actual,expected))<30,(x,y,actual,expected)
(stage/'passed.json').write_text(json.dumps({'systemCameraCapture':True,'pixelSamples':samples,'captureOutput':r.stdout},indent=2),encoding='utf-8')
(stage/'stop').write_text('test complete',encoding='utf-8')
print('PASS: Windows enumeration, capture, correct quadrant orientation and aspect padding',flush=True)
time.sleep(3)
r=subprocess.run([str(probe),'--enumerate'],capture_output=True,text=True,timeout=20)
assert r.returncode==2,'Session camera still enumerable after publisher exit'
print('PASS: session camera removed after PCam closes',flush=True)
