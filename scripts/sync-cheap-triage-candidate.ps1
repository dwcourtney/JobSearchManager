param([Parameter(Mandatory)][string]$Path,[string]$HostName)
$ErrorActionPreference='Stop'
$file=Get-Item -LiteralPath $Path
if($file.Length -gt 2000000){throw 'Candidate package exceeds 2 MB.'}
Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json | Out-Null
$options=@();if($HostName){$options=@('-o',"HostName=$HostName")}
$remote='/home/codex/jsm-lab/data/app/cheap-triage-maintenance'
& ssh @options curiosity-codex "mkdir -p $remote"
if($LASTEXITCODE){throw 'Cannot access maintenance inbox.'}
$temporary='candidate-upload-'+[guid]::NewGuid().ToString('N')+'.json'
& scp @options $file.FullName "curiosity-codex:$remote/$temporary"
if($LASTEXITCODE){throw 'Candidate transfer failed.'}
$expected=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$actual=& ssh @options curiosity-codex "sha256sum $remote/$temporary"
if($LASTEXITCODE -or !$actual.StartsWith($expected+' ')){throw 'Candidate transfer hash mismatch.'}
& ssh @options curiosity-codex "mv $remote/$temporary $remote/candidate-inbox.json"
if($LASTEXITCODE){throw 'Candidate inbox publication failed.'}
'Candidate file synced. In Admin, click Import Synced Result. Nothing was imported, approved or deployed automatically.'
