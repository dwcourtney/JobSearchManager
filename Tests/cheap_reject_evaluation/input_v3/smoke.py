import json,torch
from pathlib import Path
from transformers import AutoConfig,AutoTokenizer,AutoModelForSequenceClassification
from huggingface_hub import snapshot_download
from inputs import encode_text
root=Path(__file__).parent
cfg=json.loads((root/'protocol.json').read_text());m=cfg['models']['deberta-small']
s=snapshot_download(m['name'],revision=m['revision'],cache_dir='/tmp/jsm-deberta.dxVPsh/model-cache',local_files_only=True)
c=AutoConfig.from_pretrained(s,local_files_only=True);assert c.max_position_embeddings>=512
tok=AutoTokenizer.from_pretrained(s,local_files_only=True)
torch.set_num_threads(4)
model=AutoModelForSequenceClassification.from_pretrained(s,local_files_only=True,use_safetensors=True,num_labels=2,ignore_mismatched_sizes=True).cuda().train()
f=encode_text('Software Engineer','Build services and test code. '*2000,tok,'prefix512')[0]
assert len(f['input_ids'])==512
batch=tok.pad([f]*4,padding=True,return_tensors='pt').to('cuda')
opt=torch.optim.AdamW(model.parameters(),lr=2e-5,foreach=False)
model(**batch,labels=torch.tensor([0,1,0,1],device='cuda')).loss.backward();opt.step();torch.cuda.synchronize()
result=dict(maxPositionEmbeddings=c.max_position_embeddings,batch=4,tokens=512,trainingPeakAllocatedMiB=torch.cuda.max_memory_allocated()/2**20,trainingPeakReservedMiB=torch.cuda.max_memory_reserved()/2**20)
(root/'smoke.json').write_text(json.dumps(result,indent=2)+'\n');print(result)
