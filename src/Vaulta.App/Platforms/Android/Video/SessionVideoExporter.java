package com.vaulta.video;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.PorterDuff;
import android.graphics.Typeface;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import androidx.media3.common.MediaItem;
import androidx.media3.common.MimeTypes;
import androidx.media3.common.VideoFrameProcessingException;
import androidx.media3.effect.BitmapOverlay;
import androidx.media3.effect.OverlayEffect;
import androidx.media3.effect.Presentation;
import androidx.media3.transformer.Composition;
import androidx.media3.transformer.EditedMediaItem;
import androidx.media3.transformer.EditedMediaItemSequence;
import androidx.media3.transformer.Effects;
import androidx.media3.transformer.ExportException;
import androidx.media3.transformer.ExportResult;
import androidx.media3.transformer.ProgressHolder;
import androidx.media3.transformer.Transformer;
import com.google.common.collect.ImmutableList;
import java.io.File;
import java.text.NumberFormat;
import java.util.Collections;
import java.util.Locale;
import org.json.JSONArray;
import org.json.JSONObject;

/** Native export keeps all MAUI controls out of the video. Invoke only on the main looper. */
public final class SessionVideoExporter {
    private static Transformer transformer;
    private static String state = "idle";
    private static String error = "";
    private static String output;

    public static void start(Context context, String raw, String sound, String timeline, String destination) throws Exception {
        if (transformer != null) throw new IllegalStateException("Export already running");
        JSONObject data = new JSONObject(timeline);
        StoryOverlay overlay = new StoryOverlay(data.getJSONArray("Snapshots"), data.getBoolean("AnimateReveals"));
        EditedMediaItem video = new EditedMediaItem.Builder(MediaItem.fromUri(Uri.fromFile(new File(raw))))
            .setEffects(new Effects(Collections.emptyList(), ImmutableList.of(
                Presentation.createForWidthAndHeight(1080, 1920, Presentation.LAYOUT_SCALE_TO_FIT),
                new OverlayEffect(ImmutableList.of(overlay)))))
            .build();
        EditedMediaItem sounds = new EditedMediaItem.Builder(MediaItem.fromUri(Uri.fromFile(new File(sound)))).setRemoveVideo(true).build();
        Composition composition = new Composition.Builder(
            EditedMediaItemSequence.withAudioAndVideoFrom(ImmutableList.of(video)),
            EditedMediaItemSequence.withAudioFrom(ImmutableList.of(sounds))).build();
        output = destination; error = ""; state = "running";
        try {
            transformer = new Transformer.Builder(context.getApplicationContext())
                .setVideoMimeType(MimeTypes.VIDEO_H264).setAudioMimeType(MimeTypes.AUDIO_AAC)
                .addListener(new Transformer.Listener() {
                    @Override public void onCompleted(Composition c, ExportResult result) {
                        transformer = null;
                        try { validateOutput(output); state = "complete"; }
                        catch (Exception failure) { error = failure.getClass().getSimpleName(); state = "failed"; }
                    }
                    @Override public void onError(Composition c, ExportResult result, ExportException failure) {
                        error = "Media3:" + failure.errorCode; state = "failed"; transformer = null;
                    }
                }).build();
            transformer.start(composition, destination);
        } catch (Exception failure) {
            if (transformer != null) transformer.cancel(); transformer = null; state = "failed";
            throw failure;
        }
    }

    public static String status() {
        int progress = 0;
        if (transformer != null) {
            ProgressHolder holder = new ProgressHolder();
            if (transformer.getProgress(holder) == Transformer.PROGRESS_STATE_AVAILABLE) progress = holder.progress;
        }
        return state + "|" + progress + "|" + error;
    }

    public static void cancel() {
        if (transformer != null) transformer.cancel(); transformer = null; state = "cancelled";
    }

    private static void validateOutput(String path) throws Exception {
        try (MediaMetadataRetriever metadata = new MediaMetadataRetriever()) {
            metadata.setDataSource(path);
            int width = Integer.parseInt(metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_WIDTH));
            int height = Integer.parseInt(metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_HEIGHT));
            String rotation = metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION);
            if ("90".equals(rotation) || "270".equals(rotation)) { int swap = width; width = height; height = swap; }
            if (width >= height || Math.abs((double) width / height - 9.0 / 16) > 0.01
                || !"yes".equals(metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_HAS_AUDIO)))
                throw new IllegalStateException("Output must be a portrait video with audio");
        }
    }

    private static final class StoryOverlay extends BitmapOverlay {
        private final JSONArray snapshots;
        private final boolean animate;
        private final Bitmap bitmap = Bitmap.createBitmap(1080, 1920, Bitmap.Config.ARGB_8888);
        private final Canvas canvas = new Canvas(bitmap);
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final NumberFormat money = NumberFormat.getCurrencyInstance(new Locale("pt", "BR"));
        private int index;
        private static final int PURPLE = Color.rgb(187, 164, 255);
        private static final int GOLD = Color.rgb(255, 218, 119);
        private static final int GREEN = Color.rgb(200, 255, 221);

        StoryOverlay(JSONArray snapshots, boolean animate) { this.snapshots = snapshots; this.animate = animate; }

        @Override public Bitmap getBitmap(long timeUs) throws VideoFrameProcessingException {
            try {
                while (index + 1 < snapshots.length() && snapshots.getJSONObject(index + 1).getLong("PositionUs") <= timeUs) index++;
                while (index > 0 && snapshots.getJSONObject(index).getLong("PositionUs") > timeUs) index--;
                JSONObject current = snapshots.getJSONObject(index);
                long elapsed = Math.max(0, timeUs - current.getLong("PositionUs"));
                double value = current.getDouble("TotalValueBrl");
                double cost = current.isNull("CostBrl") ? Double.NaN : current.getDouble("CostBrl");
                boolean highlight = current.getBoolean("Highlight");
                int unpriced = current.getInt("UnpricedCards");
                canvas.drawColor(Color.TRANSPARENT, PorterDuff.Mode.CLEAR);
                panel(62, 120, 295, 230, Color.argb(225, 8, 9, 17));
                text("VAULTA", 88, 170, 44, Color.WHITE, true, 185);
                text("PACK OPENING", 88, 208, 22, PURPLE, false, 185);
                panel(562, 120, 1018, 388, Color.argb(235, 8, 9, 17));
                text("VALOR DAS CARTAS", 594, 164, 22, PURPLE, true, 385);
                double display = value;
                if (animate && index > 0 && elapsed < 700000) {
                    double prior = snapshots.getJSONObject(index - 1).getDouble("TotalValueBrl");
                    double p = 1 - Math.pow(1 - elapsed / 700000.0, 3); display = prior + (value - prior) * p;
                }
                text(money.format(display), 594, 244, 64, highlight && elapsed < 1800000 ? GOLD : Color.WHITE, true, 385);
                text(Double.isNaN(cost) ? "estimativa em reais" : "/ " + money.format(cost) + " investidos", 594, 286, 27, Color.WHITE, false, 385);
                int count = current.getInt("CardCount");
                text(count + (count == 1 ? " carta" : " cartas") + (unpriced > 0 ? " · total parcial" : ""), 594, 326, 25, PURPLE, false, 385);
                if (!Double.isNaN(cost)) text((value - cost >= 0 ? "+ " : "") + money.format(value - cost) + " estimados", 594, 366, 26, value - cost >= 0 ? GREEN : Color.rgb(255, 184, 190), false, 385);

                String name = current.optString("CardName", "");
                if (!current.isNull("CardName") && elapsed < (highlight ? 2300000 : 1400000)) {
                    panel(116, 740, 964, 1090, Color.argb(235, 17, 16, 29));
                    text(highlight ? "GRANDE ACHADO" : "CARTA REVELADA", 160, 803, 29, highlight ? GOLD : PURPLE, true, 760);
                    text(current.isNull("CardValueBrl") ? "Sem cotação" : "+ " + money.format(current.getDouble("CardValueBrl")), 160, 914, 82, highlight ? GOLD : GREEN, true, 760);
                    text(name, 160, 1000, 39, Color.WHITE, true, 760);
                    if (animate) particles(Math.min(1, elapsed / (highlight ? 1800000.0 : 1100000.0)), highlight);
                }
                panel(62, 1640, 1018, 1760, Color.argb(225, 8, 9, 17));
                text("ABERTURA · VALORES DE REFERÊNCIA", 92, 1690, 25, PURPLE, true, 900);
                text("Estimativa de mercado em R$ · não é lucro realizado", 92, 1730, 23, Color.WHITE, false, 900);
                return bitmap;
            } catch (Exception failure) { throw new VideoFrameProcessingException(failure); }
        }

        private void text(String value, float x, float y, float size, int color, boolean bold, float width) {
            paint.setColor(color); paint.setTypeface(bold ? Typeface.create("sans-serif", Typeface.BOLD) : Typeface.create("sans-serif", Typeface.NORMAL));
            paint.setTextSize(size);
            while (paint.measureText(value) > width && size > 19) { size -= 1; paint.setTextSize(size); }
            while (paint.measureText(value) > width && value.length() > 2) value = value.substring(0, value.length() - 2) + "…";
            canvas.drawText(value, x, y, paint);
        }
        private void panel(float left, float top, float right, float bottom, int color) {
            paint.setColor(color); canvas.drawRoundRect(left, top, right, bottom, 36, 36, paint);
        }
        private void particles(double progress, boolean highlight) {
            if (progress >= 1) return;
            int count = highlight ? 38 : 16;
            for (int i = 0; i < count; i++) {
                double angle = 2 * Math.PI * i / count, radius = 110 + progress * (350 + i % 3 * 45);
                paint.setColor(i % 3 == 0 ? Color.WHITE : highlight ? GOLD : PURPLE); paint.setAlpha((int)(255 * (1 - progress)));
                float x = (float)(540 + Math.cos(angle) * radius), y = (float)(900 + Math.sin(angle) * radius + progress * progress * 130);
                canvas.drawRoundRect(x, y, x + 9, y + 19, 3, 3, paint);
            }
            paint.setAlpha(255);
        }
        @Override public void release() throws VideoFrameProcessingException {
            super.release(); if (!bitmap.isRecycled()) bitmap.recycle();
        }
    }
}
