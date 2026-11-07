# Owned synthetic story media

These tiny fixtures were generated for native tests; they contain no third-party content.

`acceptance.webm`: one second of a solid blue frame (64x64, 12fps), VP8/WebM, no audio. Regenerate with:

```sh
ffmpeg -f lavfi -i color=c=0x23557b:s=64x64:r=12 -t 1 -an -c:v libvpx -b:v 40k -pix_fmt yuv420p acceptance.webm
```

`voice.wav`: one second of low-volume 440Hz mono PCM16 at 22050Hz, generated with Python's wave/math/struct modules. Native tests check playback ownership/protocol, not physical audio-device output.

Linux VP8 decoding does not establish iOS/Metal/video-codec compatibility. Production content and device acceptance remain project responsibilities.
