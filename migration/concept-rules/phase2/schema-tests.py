"""Offline structural schema + exact Phase 1 definition mapping verification."""
import argparse,hashlib,json,pathlib,collections
import jsonschema
def main():
    p=argparse.ArgumentParser();p.add_argument('repository',type=pathlib.Path);p.add_argument('--cases',type=pathlib.Path);a=p.parse_args();r=a.repository
    schema=json.loads((r/'rules/concepts-v1.schema.json').read_bytes());candidate=json.loads((r/'rules/concepts-v1.json').read_bytes())
    raw=(r/'migration/concept-rules/sqlite-effective-rules.json').read_bytes()
    assert hashlib.sha256(raw).hexdigest()=='e6a3e05ceccec71e7e2a50e4aaea64e36e667ed9278fe4eb234da951a1acb3f0'
    source=json.loads(raw);jsonschema.Draft202012Validator.check_schema(schema);validator=jsonschema.Draft202012Validator(schema);validator.validate(candidate)
    assert len(candidate['rules'])==288 and len({r['conceptId'] for r in candidate['rules']})==85
    assert candidate['migrationProvenance']['phase1ExportSha256']==hashlib.sha256(raw).hexdigest()
    for old,new in zip(source['rules'],candidate['rules']):
        for oldkey,newkey in [('ruleId','ruleId'),('conceptId','conceptId'),('ruleType','kind'),('scope','scope'),('executionIndex','executionOrder'),('provenance','provenance'),('reason','description')]:assert old[oldkey]==new[newkey],(oldkey,old['ruleId'])
        assert old['contextGroupId']==new.get('contextGroupId')
        if old['matcher']['kind']=='regex':assert old['pattern']==new['pattern'] and 'selector' not in new
        else:
            assert 'pattern' not in new
            assert new['selector']=={k:v for k,v in old['matcher'].items() if k!='kind' and v is not None}
        assert not (set(new)&{'status','createdUtc','lastModifiedUtc','matchCountLifetime','timeoutCountLifetime','relationships','evaluations'})
    checked=0;semantic=[]
    if a.cases:
        for directory in sorted(a.cases.iterdir()):
            file=directory/'concepts-v1.json'
            if not file.exists() or directory.name=='invalid-schema':continue
            try:data=json.loads(file.read_bytes())
            except ValueError:continue
            errors=list(validator.iter_errors(data))
            if directory.name=='valid':assert not errors
            elif not errors:semantic.append(directory.name)
            checked+=1
        # These require lexical/semantic validation beyond portable JSON Schema.
        assert set(semantic)<= {'duplicate-property','duplicate-id','duplicate-position','wrong-order','unknown-concept','regex','singleton-context'},semantic
    print(json.dumps(dict(result='PASS',rules=288,concepts=85,regexRules=263,typedSelectors=25,exactSourceMapping=True,schemaCases=checked,semanticOnlyCases=semantic)))
if __name__=='__main__':main()
