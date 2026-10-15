#!/bin/sh
# Runs inside an alpine container: converts every voice clip to 16-bit mono 22.05 kHz WAV in /out,
# loudness-normalized so all lines play at the same volume.
# /split holds clips cut from all.<ext>; clips recorded one per file in /in come second and win.
set -e
apk add --no-cache ffmpeg > /dev/null
for f in /split/*.wav /in/*.wav /in/*.mp3 /in/*.m4a /in/*.ogg /in/*.flac; do
    [ -e "$f" ] || continue
    name=$(basename "${f%.*}")
    [ "$name" = "all" ] && continue
    ffmpeg -loglevel error -y -i "$f" -af loudnorm=I=-16:TP=-1.5 -ac 1 -ar 22050 -sample_fmt s16 "/out/$name.wav"
    echo "voice: $name"
done
