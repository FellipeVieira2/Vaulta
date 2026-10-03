"""Reproducible ONNX encoder evaluation; transformed artwork is a smoke test, not a phone benchmark."""
import argparse, hashlib, json, math, time, urllib.request
from pathlib import Path
import numpy as np
from PIL import Image, ImageEnhance, ImageFilter, ImageOps

MODELS = {
    'dinov2-small': ('Xenova/dinov2-small', 'c2bb04a51fab207c420665f1946016107bffc701', 'onnx/model_quantized.onnx'),
    'clip-base': ('Xenova/clip-vit-base-patch32', 'd15189d7028b43f1d3e65039190477f6af591c2a', 'onnx/vision_model_quantized.onnx'),
}

def normalize_embedding(vector):
    vector = np.asarray(vector, dtype=np.float32)
    if vector.ndim != 1 or not np.isfinite(vector).all(): raise ValueError('Invalid embedding')
    norm = float(np.linalg.norm(vector))
    if norm <= 1e-12: raise ValueError('Zero embedding')
    return vector / norm

def extract_embedding(output, pooling):
    if pooling == 'cls': return normalize_embedding(output[0, 0])
    if pooling == 'pooled': return normalize_embedding(output[0])
    raise ValueError('Unknown pooling')

def rank_candidates(query, references, top_k):
    query = normalize_embedding(query)
    refs = np.asarray([normalize_embedding(v) for v in references])
    if refs.ndim != 2 or refs.shape[1] != query.size: raise ValueError('Dimension mismatch')
    return np.argsort(-(refs @ query), kind='stable')[:top_k].tolist()

def get_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'Vaulta-Vision-Evaluation/1.0'}), timeout=60) as response:
        return json.load(response)

def download(url, target, sha256=None):
    target = Path(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and (sha256 is None or hashlib.sha256(target.read_bytes()).hexdigest() == sha256): return
    part = target.with_suffix(target.suffix + '.partial')
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'Vaulta-Vision-Evaluation/1.0'}), timeout=120) as response, part.open('wb') as output:
        while chunk := response.read(1024*1024): output.write(chunk)
    if sha256 and hashlib.sha256(part.read_bytes()).hexdigest() != sha256: raise ValueError('Downloaded model checksum mismatch')
    part.replace(target)

def acquire(root):
    for name, (repo, revision, filename) in MODELS.items():
        target = root / name
        target.mkdir(parents=True, exist_ok=True)
        info = get_json(f'https://huggingface.co/api/models/{repo}/revision/{revision}?blobs=true')
        entry = next(x for x in info['siblings'] if x['rfilename'] == filename)
        expected = entry['lfs']['sha256']
        download(f'https://huggingface.co/{repo}/resolve/{revision}/{filename}', target/'model.onnx', expected)
        processor = get_json(f'https://huggingface.co/{repo}/resolve/{revision}/preprocessor_config.json')
        (target/'preprocessor.json').write_text(json.dumps(processor, indent=2))
        config = get_json(f'https://huggingface.co/{repo}/resolve/{revision}/config.json')
        (target/'config.json').write_text(json.dumps(config, indent=2))
        manifest = {'modelId': repo, 'revision': revision, 'sha256': expected, 'modelFile': 'model.onnx', 'preprocessing': processor, 'runtime': 'onnxruntime-1.23.2', 'license': info.get('cardData', {}).get('license'), 'source': f'https://huggingface.co/{repo}/tree/{revision}'}
        (target/'manifest.json').write_text(json.dumps(manifest, indent=2))
        print(json.dumps({'acquired': name, 'bytes': (target/'model.onnx').stat().st_size, 'sha256': expected}), flush=True)

def acquire_cards(root):
    cards = []
    for number in [1,2,3,4,5,6,7,8,9,10,11,12,25,58,67,69]:
        card_id = f'base1-{number}'
        card = get_json(f'https://api.tcgdex.net/v2/en/cards/{card_id}')
        download(card['image']+'/high.png', root/'cards'/f'{card_id}.png')
        cards.append({'sourceId': card_id, 'name': card['name'], 'path': f'cards/{card_id}.png', 'language': 'en', 'kind': 'official-reference'})
    (root/'cards.json').write_text(json.dumps(cards, indent=2))
    print(json.dumps({'officialReferenceCount': len(cards)}), flush=True)

def preprocess(image, processor):
    image = image.convert('RGB')
    crop = processor.get('crop_size', {})
    edge = crop.get('height', processor.get('size', {}).get('height', 224))
    if processor.get('do_center_crop', False):
        shortest = processor.get('size', {}).get('shortest_edge', edge)
        ratio = shortest/min(image.size)
        image = image.resize((round(image.width*ratio),round(image.height*ratio)), Image.Resampling.BICUBIC)
        left, top = (image.width-edge)//2, (image.height-edge)//2
        image = image.crop((left,top,left+edge,top+edge))
    else: image = image.resize((edge, edge), Image.Resampling.BICUBIC)
    pixels = np.asarray(image, dtype=np.float32) * processor.get('rescale_factor', 1/255)
    pixels = (pixels - np.asarray(processor['image_mean'], dtype=np.float32)) / np.asarray(processor['image_std'], dtype=np.float32)
    return pixels.transpose(2,0,1)[None].copy()

def evaluate(root):
    import onnxruntime as ort
    cards = json.loads((root/'cards.json').read_text())
    results = []
    for name in MODELS:
        target = root/name
        processor = json.loads((target/'preprocessor.json').read_text())
        options = ort.SessionOptions(); options.intra_op_num_threads = 2; options.inter_op_num_threads = 1
        session = ort.InferenceSession(str(target/'model.onnx'), options, providers=['CPUExecutionProvider'])
        names = [x.name for x in session.get_outputs()]
        output = 'image_embeds' if 'image_embeds' in names else 'last_hidden_state'
        pooling = 'pooled' if output == 'image_embeds' else 'cls'
        latencies = []
        def encode(image):
            tensor = preprocess(image, processor)
            started = time.perf_counter()
            vector = session.run([output], {'pixel_values': tensor})[0]
            latencies.append((time.perf_counter()-started)*1000)
            return extract_embedding(vector, pooling)
        references = [encode(Image.open(root/card['path'])) for card in cards]
        tests, top1, top5 = 0, 0, 0
        for i, card in enumerate(cards):
            original = Image.open(root/card['path']).convert('RGB')
            for transform in (lambda x:x, lambda x:ImageEnhance.Brightness(x).enhance(.65), lambda x:x.filter(ImageFilter.GaussianBlur(1.5)), lambda x:x.rotate(8, resample=Image.Resampling.BICUBIC, fillcolor='gray')):
                rank = rank_candidates(encode(transform(original)), references, 5)
                tests += 1; top1 += rank[0] == i; top5 += i in rank
        manifest = json.loads((target/'manifest.json').read_text())
        manifest.update({'inputTensor':'pixel_values','outputTensor':output,'pooling':pooling,'dimension':len(references[0]),'normalization':'l2','preprocessingVersion':'hf-pillow-bicubic-v1'})
        (target/'manifest.json').write_text(json.dumps(manifest, indent=2))
        np.save(target/'reference-embeddings.npy', np.stack(references))
        row = {'model':name,'referenceCount':len(cards),'syntheticQueries':tests,'syntheticTop1':top1/tests,'syntheticTop5':top5/tests,'inferenceP50Ms':float(np.median(latencies[1:])),'inferenceP95Ms':float(np.percentile(latencies[1:],95)),'dimension':len(references[0]),'weightsBytes':(target/'model.onnx').stat().st_size,'phonePhotoCount':0,'physicalDeviceTested':False}
        results.append(row); print(json.dumps(row), flush=True)
    (root/'evaluation.json').write_text(json.dumps(results, indent=2))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--root', type=Path, default=Path('artifacts/vision')); parser.add_argument('--acquire', action='store_true'); parser.add_argument('--cards', action='store_true'); parser.add_argument('--evaluate', action='store_true'); args=parser.parse_args()
    if args.acquire: acquire(args.root)
    if args.cards: acquire_cards(args.root)
    if args.evaluate: evaluate(args.root)