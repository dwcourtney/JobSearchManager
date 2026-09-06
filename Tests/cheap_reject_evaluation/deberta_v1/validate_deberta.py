"""Local-checkpoint sanity checks and deterministic title/truncation validation."""
import argparse
import json
from pathlib import Path
import torch
from transformers import AutoTokenizer, AutoModelForSequenceClassification
from run_deberta import make_pairs, body_slice


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--snapshot',type=Path,required=True)
    p.add_argument('--protocol',type=Path,required=True)
    p.add_argument('--input',type=Path,required=True)
    a=p.parse_args()
    cfg=json.loads(a.protocol.read_text())
    t=AutoTokenizer.from_pretrained(a.snapshot,local_files_only=True)
    model=AutoModelForSequenceClassification.from_pretrained(a.snapshot,local_files_only=True,use_safetensors=True).eval().to('cuda')
    torch.set_num_threads(4)
    premises=['A man is eating pizza.','A man is eating pizza.','A man is eating pizza.']
    hypotheses=['A man is eating food.','Nobody is eating anything.','The man has a sister.']
    reference=t(premises,hypotheses,padding=True,return_tensors='pt')
    manual=[]
    for premise,hypothesis in zip(premises,hypotheses):
        ids=t.build_inputs_with_special_tokens(t.encode(premise,add_special_tokens=False),t.encode(hypothesis,add_special_tokens=False))
        manual.append(dict(input_ids=ids,attention_mask=[1]*len(ids)))
    manual=t.pad(manual,padding=True,return_tensors='pt')
    assert torch.equal(reference['input_ids'],manual['input_ids'])
    assert torch.equal(reference['attention_mask'],manual['attention_mask'])
    with torch.inference_mode():
        first=model(**manual.to('cuda')).logits
        second=model(**reference.to('cuda')).logits
    assert torch.allclose(first,second,atol=1e-6)
    labels=[model.config.id2label[i] for i in first.argmax(-1).tolist()]
    assert labels==['entailment','contradiction','neutral'],labels
    hyp_ids=[t.encode(h,add_special_tokens=False) for h in cfg['hypotheses']]
    rows=json.loads(a.input.read_text())
    for view in cfg['views']:
        for r in rows:
            pairs,metadata=make_pairs(r,view,t,hyp_ids,cfg['max_length'])
            assert pairs is not None,'Unexpected real title overflow'
            title=t.encode('Title: '+r['title'],add_special_tokens=False)
            for pair in pairs:
                assert pair['input_ids'][1:len(title)+1]==title
                assert len(pair['input_ids'])<=512
    overflow=dict(title='word '*1000,body='body')
    assert make_pairs(overflow,'title_body_head',t,hyp_ids,512)[0] is None
    assert body_slice(list(range(10)),4,'title_body_head_tail')==[0,1,8,9]
    print(json.dumps(dict(sanityLabels=labels,tokenizerApiParity=True,logitApiParity=True,allInputsValidated=len(rows)*len(cfg['views']),fullTitlesPreserved=True,overlongTitleFailsOpen=True)))


if __name__=='__main__':
    main()
