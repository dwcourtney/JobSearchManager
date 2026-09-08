"""Candidate generation uses the verified Phase 1 export only, never the seed."""
from pathlib import Path
import json,hashlib
r=Path(__file__).resolve().parents[3]
source=r/'migration/concept-rules/sqlite-effective-rules.json'
raw=source.read_bytes();sha=hashlib.sha256(raw).hexdigest()
if sha!='e6a3e05ceccec71e7e2a50e4aaea64e36e667ed9278fe4eb234da951a1acb3f0':raise ValueError('Phase 1 export drift')
old=json.loads(raw)
policy=dict(timeoutMilliseconds=100,maximumPatternLength=4096,options=['IgnoreCase','CultureInvariant'],preferNonBacktracking=True,unsupportedRegexFallback='bounded-backtracking',ruleTimeoutBehavior='record-non-match',localNegationTimeoutBehavior='reject-match')
taxonomy=dict(version=old['taxonomyVersion'],sha256=old['taxonomyHash'])
rules=[]
for row in old['rules']:
    rule=dict(ruleId=row['ruleId'],conceptId=row['conceptId'],kind=row['ruleType'],scope=row['scope'],executionOrder=row['executionIndex'],provenance=row['provenance'],description=row['reason'])
    if row['contextGroupId'] is not None:rule['contextGroupId']=row['contextGroupId']
    if row['matcher']['kind']=='regex':rule['pattern']=row['pattern']
    else:
        rule['selector']={k:v for k,v in row['matcher'].items() if k!='kind' and v is not None}
    rules.append(rule)
definition=dict(schemaVersion=1,rulesetVersion='1.0.0',taxonomy=taxonomy,engineContract=dict(id='jsm-concept-matching',version=1),regexPolicy=policy,migrationProvenance=dict(phase1ExportSha256=sha,sqliteRuntimeFingerprint=old['runtimeFingerprint'],archiveDatabaseSha256=old['sourceDatabaseSha256']),rules=rules)
def save(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
save(r/'rules/concepts-v1.json',definition)
def obj(properties,required=None,**kwargs):return dict(type='object',additionalProperties=False,properties=properties,required=list(properties) if required is None else required,**kwargs)
text=dict(type='string',minLength=1,maxLength=300);hash_type=dict(type='string',pattern='^[a-f0-9]{64}$')
remote=json.loads((r/'rules/remote-work-v1.json').read_bytes());ext=json.loads((r/'rules/extended-location-v1.json').read_bytes())
remote_categories=sorted({x['category'] for x in remote['signalRules']+remote['travelBands']+[remote['frequentTravel']]})
extended_categories=sorted({x['category'] for x in ext['signalRules']+[ext['duration']]})
selectors=[obj(dict(source=dict(const='remoteWork.isRemoteDesignated'),operator=dict(const='isTrue'))),obj(dict(source=dict(const='remoteWork.signals'),operator=dict(const='firstCategoryEquals'),category=dict(enum=remote_categories))),obj(dict(source=dict(const='extendedLocation.signals'),operator=dict(const='firstCategoryEquals'),category=dict(enum=extended_categories)))]
common=dict(ruleId=text,conceptId=text,kind=dict(enum=sorted(old['typeCounts'])),scope=dict(enum=['title','posting','both']),executionOrder=dict(type='integer',minimum=0,maximum=9999),provenance=dict(type='string',maxLength=1000),description=dict(type='string',maxLength=4000),pattern=dict(type='string',minLength=1,maxLength=4096),contextGroupId=text,selector=dict(oneOf=selectors))
rule=obj(common,['ruleId','conceptId','kind','scope','executionOrder','provenance','description'])
rule['allOf']=[{'if':{'properties':{'kind':{'enum':['positive-evidence','title-evidence','exclusion','required-context']}}},'then':{'required':['pattern'],'not':{'required':['selector']}},'else':{'required':['selector'],'not':{'required':['pattern']}}},{'if':{'properties':{'kind':{'const':'required-context'}}},'then':{'required':['contextGroupId']},'else':{'not':{'required':['contextGroupId']}}}]
for kind,selector in zip(['remote-designation','remote-signal','extended-location-signal'],selectors):rule['allOf'].append({'if':{'properties':{'kind':{'const':kind}}},'then':{'properties':{'selector':selector}}})
rule['allOf'].append({'if':{'properties':{'kind':{'enum':['title-evidence','exclusion']}}},'then':{'properties':{'scope':{'const':'title'}}}})
schema=obj(dict(schemaVersion=dict(const=1),rulesetVersion=dict(const='1.0.0'),taxonomy=obj(dict(version=dict(const=9),sha256=dict(const=old['taxonomyHash']))),engineContract=obj(dict(id=dict(const='jsm-concept-matching'),version=dict(const=1))),regexPolicy=obj({k:dict(const=v) for k,v in policy.items()}),migrationProvenance=obj(dict(phase1ExportSha256=hash_type,sqliteRuntimeFingerprint=hash_type,archiveDatabaseSha256=hash_type)),rules=dict(type='array',minItems=1,maxItems=10000,items=rule)))
schema={'$schema':'https://json-schema.org/draft/2020-12/schema','$id':'https://jobsearchmanager.local/schemas/concepts-v1.schema.json','title':'Migration-only concept rules v1',**schema}
schema['$comment']='Loader additionally validates unique IDs/positions, canonical contiguous SQL order, taxonomy membership, regex compilation, and context groups.'
save(r/'rules/concepts-v1.schema.json',schema)
print(json.dumps(dict(rules=len(rules),kinds=old['typeCounts'],sha256=hashlib.sha256((r/'rules/concepts-v1.json').read_bytes()).hexdigest())))
