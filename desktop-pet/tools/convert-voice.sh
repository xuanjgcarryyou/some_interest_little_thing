#!/bin/sh
# Runs inside an alpine container: converts every voice clip in /in to 16-bit mono 22.05 kHz WAV in /out,
# loudness-normalized so all lines play at the same volume.
set -e
apk add --no-cache ffmpeg > /dev/null
for f in /in/*.wav /in/*.mp3 /in/*.m4a /in/*.ogg /in/*.flac; do
    [ -e "$f" ] || continue
    name=$(basename "${f%.*}")
    ffmpeg -loglevel error -y -i "$f" -af loudnorm=I=-16:TP=-1.5 -ac 1 -ar 22050 -sample_fmt s16 "/out/$name.wav"
    echo "voice: $name"
done
