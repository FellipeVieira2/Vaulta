# Vision model evaluation — 2026-10-03

Two pinned, real quantized ONNX image encoders were downloaded and checksum-verified. CPU inference used ONNX Runtime 1.23.2, two intra-op threads, Windows x64. Sixteen official English Base Set images produced 64 synthetic queries per encoder (original, brightness, blur and rotation). These are pipeline smoke tests, not phone-photo accuracy.

| Encoder | Weights | Dimension | p50 | p95 | Synthetic Top-1/5 |
|---|---:|---:|---:|---:|---:|
| Xenova/dinov2-small @ c2bb04a51fab207c420665f1946016107bffc701 | 24,451,943 bytes | 384 | 133.3 ms | 150.2 ms | 64/64 |
| Xenova/clip-vit-base-patch32 @ d15189d7028b43f1d3e65039190477f6af591c2a | 89,117,001 bytes | 512 | 66.8 ms | 78.1 ms | 64/64 |

Initial choice: CLIP quantized image encoder. It was faster on the measured CPU and provides a shared image/text representation for zero-shot orientation prototypes. This does not prove it is more accurate on real cards. DINOv2 remains a replacement candidate behind the encoder interface.

CLIP input: pixel_values, float32 NCHW, RGB, shortest edge 224, bicubic center crop 224x224; mean [0.48145466,0.4578275,0.40821073], std [0.26862954,0.26130258,0.27577711]; output image_embeds [batch,512], L2 normalized. Artifact manifests record weights SHA256 and preprocessing. DINOv2 has last_hidden_state [batch,257,384]; use CLS, not a fictitious pooler_output.

Orientation uses averaged, normalized CLIP text prototypes (front/back/no-card), not supervised training. Fifteen of sixteen official front references ranked front first; one did not. Margins are small on some images. Therefore uncertain orientation remains unknown and cannot block identity or trigger a claim of calibrated 80% confidence. Physical backs, sleeves and non-card scenes still require validation.

No phone connected to adb. Available AVDs: pixel_7_-_api_36_0 and pixel_9_-_api_35_0. Android inference/build/harness validation belongs to the camera integration task, after its bridge exists. No physical-device latency/accuracy claimed.

Baseline: Windows App Control blocked 47 assembly loads (0x800711C7); no security policy was changed. Same source passed 288/288 unit tests in the isolated Linux SDK container. Further server tests use that container.

Reproduce: isolated Python 3.12 environment with onnxruntime==1.23.2, numpy and Pillow; `python scripts/vision/evaluation.py --acquire --cards --evaluate`. Orientation probe additionally uses tokenizers==0.22.1. Models remain under ignored artifacts/vision; never commit weights or credentials.