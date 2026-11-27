#!/usr/bin/env python3
"""
Lightweight, universal Whisper transcription script for MeetingCapture.
Supports all WAV formats (PCM 16/24/32-bit, IEEE Float 32/64-bit, WAVE_FORMAT_EXTENSIBLE).
Does not depend on external ffmpeg CLI.
Outputs JSON formatted segments to stdout.
"""
import sys
import os
import json
import struct
import numpy as np

def load_wav_to_float32(wav_path, target_sr=16000):
    """
    Universally loads any WAV file (PCM 8/16/24/32-bit, IEEE float 32/64-bit, Extensible)
    downmixes to mono, and resamples to target_sr (16000 Hz).
    """
    with open(wav_path, "rb") as f:
        header = f.read(12)
        if len(header) < 12:
            return np.zeros(0, dtype=np.float32)

        riff, file_size, wave = struct.unpack("<4sI4s", header)
        if riff != b"RIFF" or wave != b"WAVE":
            raise ValueError(f"Not a valid RIFF/WAVE file: {wav_path}")

        fmt_tag = 1
        channels = 1
        framerate = 16000
        bits_per_sample = 16
        raw_data = None

        while True:
            chunk_hdr = f.read(8)
            if len(chunk_hdr) < 8:
                break
            chunk_id, chunk_size = struct.unpack("<4sI", chunk_hdr)

            if chunk_id == b"fmt ":
                fmt_bytes = f.read(chunk_size)
                fmt_tag, channels, framerate, _, _, bits_per_sample = struct.unpack("<HHIIHH", fmt_bytes[:16])
                # WAVE_FORMAT_EXTENSIBLE (0xFFFE)
                if fmt_tag == 0xFFFE and len(fmt_bytes) >= 26:
                    sub_tag = struct.unpack("<H", fmt_bytes[24:26])[0]
                    fmt_tag = sub_tag
            elif chunk_id == b"data":
                raw_data = f.read(chunk_size)
                break
            else:
                f.seek(chunk_size, 1)

    if not raw_data or len(raw_data) == 0:
        return np.zeros(0, dtype=np.float32)

    # Convert bytes to float32 (-1.0 .. 1.0)
    if fmt_tag == 3:  # IEEE Float
        if bits_per_sample == 32:
            audio_data = np.frombuffer(raw_data, dtype=np.float32).copy()
        elif bits_per_sample == 64:
            audio_data = np.frombuffer(raw_data, dtype=np.float64).astype(np.float32)
        else:
            raise ValueError(f"Unsupported float bit depth: {bits_per_sample}")
    elif fmt_tag == 1:  # Integer PCM
        if bits_per_sample == 16:
            audio_data = (np.frombuffer(raw_data, dtype=np.int16) / 32768.0).astype(np.float32)
        elif bits_per_sample == 32:
            audio_data = (np.frombuffer(raw_data, dtype=np.int32) / 2147483648.0).astype(np.float32)
        elif bits_per_sample == 24:
            # 24-bit PCM: 3 bytes per sample
            raw_bytes = np.frombuffer(raw_data, dtype=np.uint8)
            n_samples = len(raw_bytes) // 3
            raw_bytes = raw_bytes[:n_samples * 3].reshape(-1, 3)
            # Pad with 0 as LSB to create 32-bit signed ints
            padded = np.zeros((n_samples, 4), dtype=np.uint8)
            padded[:, 1:] = raw_bytes
            int32_arr = padded.view(dtype=np.int32).reshape(-1)
            audio_data = (int32_arr / 2147483648.0).astype(np.float32)
        elif bits_per_sample == 8:
            audio_data = ((np.frombuffer(raw_data, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0)
        else:
            raise ValueError(f"Unsupported PCM bit depth: {bits_per_sample}")
    else:
        raise ValueError(f"Unsupported WAV format tag: {fmt_tag}")

    # Downmix multi-channel to mono
    if channels > 1:
        n_frames = len(audio_data) // channels
        audio_data = audio_data[:n_frames * channels].reshape(-1, channels).mean(axis=1)

    # Resample to 16000 Hz if needed
    if framerate != target_sr and len(audio_data) > 0:
        duration = len(audio_data) / framerate
        target_len = int(duration * target_sr)
        if target_len > 0:
            indices = np.linspace(0, len(audio_data) - 1, target_len)
            audio_data = np.interp(indices, np.arange(len(audio_data)), audio_data).astype(np.float32)

    return audio_data

def transcribe_file(wav_path, model_name="tiny", language="fr"):
    if not os.path.isfile(wav_path):
        print(json.dumps({"error": f"File not found: {wav_path}"}))
        return 1

    try:
        audio_array = load_wav_to_float32(wav_path, 16000)
    except Exception as ex:
        sys.stderr.write(f"Error loading WAV: {ex}\n")
        print(json.dumps([]))
        return 0

    # If audio is empty or near total silence
    if len(audio_array) < 8000 or np.abs(audio_array).max() < 0.0005:
        print(json.dumps([]))
        return 0

    try:
        import whisper
        model = whisper.load_model(model_name)
        result = model.transcribe(audio_array, language=language, fp16=False)
    except Exception as ex:
        sys.stderr.write(f"Whisper inference error: {ex}\n")
        print(json.dumps([]))
        return 0

    segments_out = []
    raw_segments = result.get("segments", [])

    if not raw_segments and result.get("text", "").strip():
        segments_out.append({
            "Start": 0.0,
            "End": round(len(audio_array) / 16000.0, 2),
            "Speaker": "Intervenant",
            "Text": result["text"].strip(),
            "Confidence": 0.95,
            "Language": language
        })
    else:
        for idx, seg in enumerate(raw_segments):
            text = seg.get("text", "").strip()
            if not text:
                continue
            confidence = 0.95
            try:
                confidence = round(float(np.exp(seg.get("avg_logprob", -0.1))), 2)
            except Exception:
                pass

            segments_out.append({
                "Start": round(float(seg.get("start", 0.0)), 2),
                "End": round(float(seg.get("end", 0.0)), 2),
                "Speaker": f"Intervenant {1 + (idx % 2)}",
                "Text": text,
                "Confidence": min(1.0, max(0.0, confidence)),
                "Language": language
            })

    print(json.dumps(segments_out, ensure_ascii=False))
    return 0

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"error": "Usage: transcribe_wav.py <path_to_wav> [model_name] [language]"}))
        sys.exit(1)

    wav_file = sys.argv[1]
    model = sys.argv[2] if len(sys.argv) > 2 else "tiny"
    lang = sys.argv[3] if len(sys.argv) > 3 else "fr"
    sys.exit(transcribe_file(wav_file, model, lang))
