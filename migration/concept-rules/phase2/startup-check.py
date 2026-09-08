"""Launch migration-only diagnostic host; rejected candidates must exit before listening."""
import argparse,pathlib,subprocess,socket,time,urllib.request,json,uuid
def main():
    p=argparse.ArgumentParser();p.add_argument('dll',type=pathlib.Path);p.add_argument('cases',type=pathlib.Path);p.add_argument('output',type=pathlib.Path);p.add_argument('--image');a=p.parse_args();results=[]
    for directory in sorted(a.cases.iterdir()):
        with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
        url=f'http://127.0.0.1:{port}'
        with (directory/'host.log').open('w') as log:
            name='jsm-json-startup-'+uuid.uuid4().hex[:12]
            command=(['docker','run','--rm','--name',name,'--read-only','--tmpfs','/tmp','--cap-drop','ALL','--security-opt','no-new-privileges:true','-p',f'127.0.0.1:{port}:8080','-v',str(directory.resolve())+':/candidate:ro',a.image,'--json-concept-host','/candidate/concepts-v1.json','http://0.0.0.0:8080'] if a.image else ['dotnet',str(a.dll.resolve()),'--json-concept-host',str((directory/'concepts-v1.json').resolve()),url])
            process=subprocess.Popen(command,cwd=None if a.image else a.dll.resolve().parent,stdout=log,stderr=subprocess.STDOUT)
            try:
                health=False;deadline=time.monotonic()+20
                while time.monotonic()<deadline:
                    try:
                        with urllib.request.urlopen(url+'/healthz',timeout=.2) as response:health=response.status==200 and response.read()==b'Healthy'
                    except (OSError,TimeoutError):pass
                    if health or process.poll() is not None:break
                    time.sleep(.05)
                if directory.name=='valid':
                    assert health and process.poll() is None,'Valid candidate did not listen'
                    with urllib.request.urlopen(url+'/version') as response:identity=json.load(response)
                    assert identity['version']=='1.0.0' and len(identity['pipelineFingerprint'])==64
                else:
                    assert not health and process.poll() is not None and process.returncode!=0,'Invalid candidate listened or hung: '+directory.name
                    with socket.socket() as s:assert s.connect_ex(('127.0.0.1',port))!=0
                results.append(dict(case=directory.name,result='PASS',listened=health,exitCode=process.poll()))
            finally:
                if a.image:subprocess.run(['docker','rm','-f',name],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                if process.poll() is None:process.terminate();process.wait(timeout=10)
    a.output.write_text(json.dumps(results,indent=2)+'\n');print(f'PASS {len(results)} pre-host validation cases')
if __name__=='__main__':main()
