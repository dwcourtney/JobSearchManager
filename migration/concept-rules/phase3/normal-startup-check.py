"""Exercise the actual normal host; no diagnostic-host shortcut or SQLite fallback."""
import argparse,ctypes,hashlib,json,os,pathlib,shutil,socket,subprocess,time,urllib.request,uuid
p=argparse.ArgumentParser();p.add_argument('source',type=pathlib.Path);p.add_argument('cases',type=pathlib.Path);p.add_argument('output',type=pathlib.Path);p.add_argument('--publish',type=pathlib.Path);p.add_argument('--image');a=p.parse_args()
assert bool(a.publish)!=bool(a.image)
if os.name=='nt':ctypes.windll.kernel32.SetErrorMode(3) # Suppress inherited OS crash dialogs, not validation failures.
missing=a.cases/'missing-schema';missing.mkdir(exist_ok=True)
shutil.copyfile(a.cases/'valid/concepts-v1.json',missing/'concepts-v1.json')
if (missing/'concepts-v1.schema.json').exists():(missing/'concepts-v1.schema.json').unlink()
cases=sorted(d for d in a.cases.iterdir() if d.is_dir() and not d.name.startswith('_'))
host=a.cases/'_normal-host'
if a.publish:shutil.copytree(a.publish,host,dirs_exist_ok=True)
results=[]
for case in cases:
    name='jsm-json-normal-startup-'+uuid.uuid4().hex[:12]
    rules=(case/'_normal/rules') if a.image else host/'rules'
    if a.image:shutil.copytree(a.source/'rules',rules,dirs_exist_ok=True)
    for file in ['concepts-v1.json','concepts-v1.schema.json']:
        if (rules/file).exists():(rules/file).unlink()
        if (case/file).exists():shutil.copyfile(case/file,rules/file)
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
    url=f'http://127.0.0.1:{port}'
    if a.image:
        command=['docker','run','--rm','--name',name,'--read-only','--tmpfs','/tmp','--tmpfs','/app/data:mode=0777','--tmpfs','/keys:mode=0777','--cap-drop','ALL','--security-opt','no-new-privileges:true','-p',f'127.0.0.1:{port}:8080','-v',str(rules.resolve())+':/app/rules:ro','-e','JOBSEARCHMANAGER_HOSTING_MODE=Container','-e','JOBSEARCHMANAGER_DATA_PROTECTION_PATH=/keys','-e','ASPNETCORE_URLS=http://0.0.0.0:8080',a.image]
        env=None;cwd=None
    else:
        command=['dotnet',str((host/'JobSearchManager.dll').resolve())];cwd=host
        env=dict(os.environ,JOBSEARCHMANAGER_HOSTING_MODE='Container',JOBSEARCHMANAGER_DATA_PROTECTION_PATH=str((host/'keys').resolve()),ASPNETCORE_URLS=url,Application__RefreshOnStartup='false',Application__OpenBrowser='false')
        data=host/'data';data.mkdir(exist_ok=True);(data/'regex-rules.db').write_bytes(b'not a SQLite database: normal JSON authority must never read this')
    with (case/'normal-host.log').open('w',encoding='utf-8') as log:
        proc=subprocess.Popen(command,cwd=cwd,env=env,stdout=log,stderr=subprocess.STDOUT)
        try:
            healthy=False;deadline=time.monotonic()+30
            while time.monotonic()<deadline:
                try:
                    with urllib.request.urlopen(url+'/healthz',timeout=.2) as response:healthy=response.status==200 and response.read()==b'Healthy'
                except (OSError,TimeoutError):pass
                if healthy or proc.poll() is not None:break
                time.sleep(.05)
            if case.name=='valid':
                assert healthy and proc.poll() is None,'Valid normal host did not listen'
                with urllib.request.urlopen(url+'/version') as response:version=json.load(response)
                with urllib.request.urlopen(url+'/api/jobs') as response:assert json.load(response)['jobs']==[]
                if not a.image:assert (data/'regex-rules.db').read_bytes()==b'not a SQLite database: normal JSON authority must never read this'
            else:
                assert not healthy and proc.poll() is not None and proc.returncode!=0,'Invalid normal host listened or hung: '+case.name
                with socket.socket() as sock:assert sock.connect_ex(('127.0.0.1',port))!=0
            results.append(dict(case=case.name,result='PASS',listened=healthy,exitCode=proc.poll(),normalHost=True))
        finally:
            if a.image:subprocess.run(['docker','rm','-f',name],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            if proc.poll() is None:proc.terminate();proc.wait(timeout=15)
a.output.write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')
print(f'PASS {len(results)} actual normal-host startup cases; invalid rules never listen; no SQLite fallback')
