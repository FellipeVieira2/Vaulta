import json, time
from pathlib import Path
import numpy as np
import onnxruntime as ort
from PIL import Image
from tokenizers import Tokenizer
from evaluation import download,get_json,preprocess,normalize_embedding
root=Path('artifacts/vision'); base=root/'clip-base'; repo='Xenova/clip-vit-base-patch32'; rev='d15189d7028b43f1d3e65039190477f6af591c2a'
info=get_json(f'https://huggingface.co/api/models/{repo}/revision/{rev}?blobs=true')
f='onnx/text_model_quantized.onnx'; expected=next(x['lfs']['sha256'] for x in info['siblings'] if x['rfilename']==f)
download(f'https://huggingface.co/{repo}/resolve/{rev}/{f}',base/'text.onnx',expected)
download(f'https://huggingface.co/{repo}/resolve/{rev}/tokenizer.json',base/'tokenizer.json')
tok=Tokenizer.from_file(str(base/'tokenizer.json')); tok.enable_truncation(max_length=77); tok.enable_padding(length=77,pad_id=49407,pad_token='<|endoftext|>')
labels={'front':['a photo of the front of a Pokemon trading card','a collectible trading card with artwork and printed text','the front of a Yu-Gi-Oh or One Piece card'], 'back':['a photo of the back of a Pokemon trading card','the back side of a collectible trading card with a logo and no card name','a trading card turned face down'], 'no-card':['a photo of an empty table without any trading cards','a hand without a trading card','a photo of household objects, no cards']}
text=ort.InferenceSession(str(base/'text.onnx'),providers=['CPUExecutionProvider']); print('text tensors',[(x.name,x.shape) for x in text.get_inputs()],[(x.name,x.shape) for x in text.get_outputs()],flush=True)
prototypes={}
for label,prompts in labels.items():
    encoded=tok.encode_batch(prompts)
    feed={'input_ids':np.array([x.ids for x in encoded],dtype=np.int64),'attention_mask':np.array([x.attention_mask for x in encoded],dtype=np.int64)}
    feed={k:v for k,v in feed.items() if k in [x.name for x in text.get_inputs()]}
    embeddings=text.run(['text_embeds'],feed)[0]
    prototypes[label]=normalize_embedding(np.mean([normalize_embedding(x) for x in embeddings],axis=0)).tolist()
(base/'orientation-prototypes.json').write_text(json.dumps({'modelId':repo,'revision':rev,'dimension':512,'labels':prototypes,'method':'zero-shot-text-prototypes','promptVersion':'vaulta-orientation-v1','textModelSha256':expected},indent=2))
refs=np.load(base/'reference-embeddings.npy'); scores=refs @ np.array(list(prototypes.values()),dtype=np.float32).T
print(json.dumps({'officialFrontCount':len(refs),'frontTop1Count':int((np.argmax(scores,axis=1)==0).sum()),'scoresFirstCard':scores[0].tolist(),'physicalBackImages':0}),flush=True)