package il.shamai.field

import android.Manifest
import android.annotation.SuppressLint
import android.content.ContentValues
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.location.Geocoder
import android.location.Location
import android.location.LocationManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.MediaStore
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import android.util.Base64
import android.webkit.JavascriptInterface
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import androidx.core.location.LocationManagerCompat
import androidx.exifinterface.media.ExifInterface
import androidx.webkit.WebViewAssetLoader
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.util.Locale
import java.util.UUID
import java.util.concurrent.Executors

/**
 * מעטפת אנדרואיד לאפליקציית השטח.
 * הממשק עצמו הוא דף HTML מקומי (assets/www/index.html) שרץ בתוך WebView,
 * בדומה למבנה של HandiMate. מחלקה זו מוסיפה לו יכולות של הטלפון דרך גשר JavaScript בשם Native:
 *   מצלמה ובחירה מהגלריה, זיהוי לוחית על המכשיר (ML Kit), מיקום GPS עם כתובת,
 *   הכתבה בעברית, שמירת תמונות באחסון הפנימי, שמירת קבצים להורדות ושיתוף.
 * הכול נשמר בטלפון בלבד. אין שרת.
 */
class MainActivity : ComponentActivity() {

    private lateinit var web: WebView
    private val host = "appassets.androidplatform.net"
    private val blobDir by lazy { File(filesDir, "blobs").apply { mkdirs() } }
    private val io = Executors.newSingleThreadExecutor()
    private val net = Executors.newSingleThreadExecutor()
    @Volatile private var userAgent: String = "Mozilla/5.0 (Linux; Android) FieldCase"

    // file chooser (<input type=file>)
    private var fileCallback: ValueCallback<Array<Uri>>? = null
    private var cameraUri: Uri? = null

    // plate
    private var plateCb: String? = null
    private var plateUri: Uri? = null

    // permissions
    private var permCallback: ((Map<String, Boolean>) -> Unit)? = null

    // speech
    private var speech: SpeechRecognizer? = null
    private var dictStartCb: String? = null

    private val permLauncher =
        registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { res ->
            val cb = permCallback; permCallback = null; cb?.invoke(res)
        }
    private val chooserCamera =
        registerForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
            val u = cameraUri
            fileCallback?.onReceiveValue(if (ok && u != null) arrayOf(u) else null)
            fileCallback = null
        }
    private val chooserGallery =
        registerForActivityResult(ActivityResultContracts.GetMultipleContents()) { uris ->
            fileCallback?.onReceiveValue(uris.toTypedArray())
            fileCallback = null
        }
    private val plateCamera =
        registerForActivityResult(ActivityResultContracts.TakePicture()) { ok -> onPlatePhoto(ok) }

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        web = WebView(this)
        setContentView(web)

        val loader = WebViewAssetLoader.Builder()
            .setDomain(host)
            .addPathHandler("/assets/", WebViewAssetLoader.AssetsPathHandler(this))
            .addPathHandler("/blobs/", WebViewAssetLoader.InternalStoragePathHandler(this, blobDir))
            .build()

        web.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            databaseEnabled = true
            allowFileAccess = false
            allowContentAccess = true
            mediaPlaybackRequiresUserGesture = true
        }
        userAgent = web.settings.userAgentString ?: userAgent
        web.addJavascriptInterface(Bridge(), "Native")
        web.webViewClient = object : WebViewClient() {
            override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? =
                loader.shouldInterceptRequest(request.url)

            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val u = request.url
                if (u.host == host) return false
                // קישורים חיצוניים (טלפון, מייל, אתרים) נפתחים מחוץ לאפליקציה
                try { startActivity(Intent(Intent.ACTION_VIEW, u)) } catch (_: Exception) {}
                return true
            }
        }
        web.webChromeClient = object : WebChromeClient() {
            override fun onShowFileChooser(
                view: WebView, callback: ValueCallback<Array<Uri>>, params: FileChooserParams
            ): Boolean {
                fileCallback?.onReceiveValue(null)
                fileCallback = callback
                if (params.isCaptureEnabled) openCameraForChooser() else chooserGallery.launch("image/*")
                return true
            }
        }

        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                web.evaluateJavascript("(window.__back ? window.__back() : false)") { r ->
                    if (r != "true") finish()
                }
            }
        })

        if (savedInstanceState != null) web.restoreState(savedInstanceState)
        else web.loadUrl("https://$host/assets/www/index.html")
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        web.saveState(outState)
    }

    override fun onDestroy() {
        speech?.destroy()
        io.shutdown()
        net.shutdown()
        super.onDestroy()
    }

    // ---------- helpers ----------

    private fun js(code: String) = runOnUiThread { web.evaluateJavascript(code, null) }

    private fun resolve(cb: String, json: String) = js("window.__nb(${JSONObject.quote(cb)},true,$json)")
    private fun reject(cb: String, code: String) =
        js("window.__nb(${JSONObject.quote(cb)},false,${JSONObject().put("code", code)})")

    private fun withPermissions(perms: Array<String>, then: (Map<String, Boolean>) -> Unit) {
        val missing = perms.filter {
            ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED
        }
        if (missing.isEmpty()) { then(perms.associateWith { true }); return }
        permCallback = { res -> then(perms.associateWith { p -> res[p] ?: (ContextCompat.checkSelfPermission(this, p) == PackageManager.PERMISSION_GRANTED) }) }
        permLauncher.launch(missing.toTypedArray())
    }

    private fun newCacheImageUri(): Uri {
        val dir = File(cacheDir, "camera").apply { mkdirs() }
        val f = File.createTempFile("img_", ".jpg", dir)
        return FileProvider.getUriForFile(this, "$packageName.files", f)
    }

    private fun openCameraForChooser() {
        withPermissions(arrayOf(Manifest.permission.CAMERA)) { res ->
            if (res[Manifest.permission.CAMERA] != true) {
                fileCallback?.onReceiveValue(null); fileCallback = null
                js("window.__toast && window.__toast('אין הרשאת מצלמה. אפשר לאשר בהגדרות הטלפון.')")
                return@withPermissions
            }
            val u = newCacheImageUri(); cameraUri = u
            try { chooserCamera.launch(u) } catch (e: Exception) {
                fileCallback?.onReceiveValue(null); fileCallback = null
            }
        }
    }

    /** קורא תמונה, מסובב לפי EXIF, מקטין לצלע ארוכה של max ומחזיר JPEG */
    private fun loadScaledJpeg(uri: Uri, max: Int = 1600, quality: Int = 82): ByteArray? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
        if (bounds.outWidth <= 0) return null
        var sample = 1
        while (maxOf(bounds.outWidth, bounds.outHeight) / (sample * 2) >= max) sample *= 2
        var bmp = contentResolver.openInputStream(uri)?.use {
            BitmapFactory.decodeStream(it, null, BitmapFactory.Options().apply { inSampleSize = sample })
        } ?: return null
        val rot = contentResolver.openInputStream(uri)?.use {
            when (ExifInterface(it).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)) {
                ExifInterface.ORIENTATION_ROTATE_90 -> 90f
                ExifInterface.ORIENTATION_ROTATE_180 -> 180f
                ExifInterface.ORIENTATION_ROTATE_270 -> 270f
                else -> 0f
            }
        } ?: 0f
        val scale = minOf(1f, max.toFloat() / maxOf(bmp.width, bmp.height))
        if (rot != 0f || scale < 1f) {
            val m = Matrix().apply { postScale(scale, scale); postRotate(rot) }
            bmp = Bitmap.createBitmap(bmp, 0, 0, bmp.width, bmp.height, m, true)
        }
        return ByteArrayOutputStream().use { out ->
            bmp.compress(Bitmap.CompressFormat.JPEG, quality, out); out.toByteArray()
        }
    }

    // ---------- plate (ML Kit, on device) ----------

    private fun startPlate(cb: String) {
        withPermissions(arrayOf(Manifest.permission.CAMERA)) { res ->
            if (res[Manifest.permission.CAMERA] != true) { reject(cb, "not_granted"); return@withPermissions }
            val u = newCacheImageUri()
            plateCb = cb; plateUri = u
            try { plateCamera.launch(u) } catch (e: Exception) { plateCb = null; reject(cb, "no_camera") }
        }
    }

    private fun onPlatePhoto(ok: Boolean) {
        val cb = plateCb ?: return
        val uri = plateUri
        plateCb = null; plateUri = null
        if (!ok || uri == null) { resolve(cb, """{"cancelled":true}"""); return }
        val image = try { InputImage.fromFilePath(this, uri) } catch (e: Exception) { reject(cb, "bad_image"); return }
        TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS).process(image)
            .addOnSuccessListener { text ->
                io.execute {
                    // מועמדים: שורות שנראות כמו לוחית (7 או 8 ספרות, עם מקפים/רווחים/נקודות)
                    val plateLike = Regex("^\\D{0,3}(\\d{2,3}[-. ]?\\d{2,3}[-. ]?\\d{2,3})\\D{0,3}$")
                    var best: String? = null; var bestH = -1
                    for (block in text.textBlocks) for (line in block.lines) {
                        val raw = line.text.trim()
                        val m = plateLike.find(raw) ?: continue
                        val d = m.groupValues[1].filter { it.isDigit() }
                        if (d.length != 7 && d.length != 8) continue
                        val h = line.boundingBox?.height() ?: 0
                        if (h > bestH) { best = d; bestH = h }
                    }
                    var confidence = "high"
                    if (best == null) {
                        val all = text.text.filter { it.isDigit() }
                        if (all.length == 7 || all.length == 8) { best = all; confidence = "low" }
                    }
                    val jpeg = try { loadScaledJpeg(uri) } catch (e: Exception) { null }
                    val json = JSONObject()
                        .put("plate", best ?: JSONObject.NULL)
                        .put("confidence", if (best == null) "low" else confidence)
                        .put("image", jpeg?.let { Base64.encodeToString(it, Base64.NO_WRAP) } ?: JSONObject.NULL)
                    resolve(cb, json.toString())
                }
            }
            .addOnFailureListener { reject(cb, "ocr_failed") }
    }

    // ---------- location ----------

    @SuppressLint("MissingPermission")
    private fun startLocation(cb: String) {
        val perms = arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
        withPermissions(perms) { res ->
            if (res.values.none { it }) { reject(cb, "not_granted"); return@withPermissions }
            val lm = getSystemService(LocationManager::class.java)
            val provider = when {
                res[Manifest.permission.ACCESS_FINE_LOCATION] == true &&
                    lm.isProviderEnabled(LocationManager.GPS_PROVIDER) -> LocationManager.GPS_PROVIDER
                lm.isProviderEnabled(LocationManager.NETWORK_PROVIDER) -> LocationManager.NETWORK_PROVIDER
                lm.isProviderEnabled(LocationManager.GPS_PROVIDER) -> LocationManager.GPS_PROVIDER
                else -> null
            }
            if (provider == null) { reject(cb, "location_off"); return@withPermissions }
            LocationManagerCompat.getCurrentLocation(
                lm, provider, android.os.CancellationSignal(), ContextCompat.getMainExecutor(this),
                androidx.core.util.Consumer<Location?> { loc ->
                    val l = loc
                        ?: lm.getLastKnownLocation(provider)
                        ?: lm.getLastKnownLocation(LocationManager.PASSIVE_PROVIDER)
                    if (l == null) { reject(cb, "no_fix"); return@Consumer }
                    io.execute {
                        val address = try {
                            @Suppress("DEPRECATION")
                            Geocoder(this, Locale("he", "IL")).getFromLocation(l.latitude, l.longitude, 1)
                                ?.firstOrNull()?.getAddressLine(0)
                        } catch (e: Exception) { null }
                        val json = JSONObject()
                            .put("lat", l.latitude).put("lng", l.longitude)
                            .put("acc", l.accuracy.toDouble()).put("ts", l.time)
                            .put("address", address ?: JSONObject.NULL)
                        resolve(cb, json.toString())
                    }
                }
            )
        }
    }

    // ---------- dictation (Hebrew) ----------

    private fun dict(type: String, text: String = "") =
        js("window.__dict && window.__dict(${JSONObject.quote(type)},${JSONObject.quote(text)})")

    private val listener = object : RecognitionListener {
        override fun onReadyForSpeech(params: Bundle?) { dict("listening") }
        override fun onBeginningOfSpeech() {}
        override fun onRmsChanged(rmsdB: Float) {}
        override fun onBufferReceived(buffer: ByteArray?) {}
        override fun onEndOfSpeech() {}
        override fun onError(error: Int) {
            dict("idle", if (error == SpeechRecognizer.ERROR_NO_MATCH || error == SpeechRecognizer.ERROR_SPEECH_TIMEOUT) "no_speech" else "error")
        }
        override fun onResults(results: Bundle?) {
            val t = results?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.firstOrNull()
            if (!t.isNullOrBlank()) dict("final", t)
            dict("idle", "stopped")
        }
        override fun onPartialResults(partialResults: Bundle?) {
            val t = partialResults?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.firstOrNull()
            if (!t.isNullOrBlank()) dict("partial", t)
        }
        override fun onEvent(eventType: Int, params: Bundle?) {}
    }

    private fun startDictation() {
        withPermissions(arrayOf(Manifest.permission.RECORD_AUDIO)) { res ->
            if (res[Manifest.permission.RECORD_AUDIO] != true) { dict("error", "not_granted"); return@withPermissions }
            if (!SpeechRecognizer.isRecognitionAvailable(this)) { dict("error", "unavailable"); return@withPermissions }
            val sr = speech ?: SpeechRecognizer.createSpeechRecognizer(this).also {
                it.setRecognitionListener(listener); speech = it
            }
            val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                putExtra(RecognizerIntent.EXTRA_LANGUAGE, "he-IL")
                putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true)
                putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, 2500L)
            }
            sr.cancel()
            sr.startListening(intent)
        }
    }

    // ---------- files ----------

    private fun safeName(name: String) = name.replace(Regex("[\\\\/:*?\"<>|\\n\\r]"), "_").take(120)

    private fun saveToDownloads(name: String, mime: String, bytes: ByteArray): String {
        val n = safeName(name)
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val values = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, n)
                put(MediaStore.Downloads.MIME_TYPE, mime)
                put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/FieldCase")
            }
            val uri = contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
                ?: throw IllegalStateException("insert failed")
            contentResolver.openOutputStream(uri)?.use { it.write(bytes) }
            "הורדות/FieldCase/$n"
        } else {
            val dir = getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS) ?: filesDir
            File(dir, n).writeBytes(bytes)
            "${dir.absolutePath}/$n"
        }
    }

    private fun share(name: String, mime: String, bytes: ByteArray) {
        val dir = File(cacheDir, "share").apply { mkdirs() }
        val f = File(dir, safeName(name)).apply { writeBytes(bytes) }
        val uri = FileProvider.getUriForFile(this, "$packageName.files", f)
        val send = Intent(Intent.ACTION_SEND).apply {
            type = mime
            putExtra(Intent.EXTRA_STREAM, uri)
            putExtra(Intent.EXTRA_SUBJECT, name)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        runOnUiThread { startActivity(Intent.createChooser(send, "שתף")) }
    }

    // ---------- vehicle registry (data.gov.il) ----------

    /** שולף רשומת רכב ממאגר משרד התחבורה ומחזיר את תשובת ה-API כמחרוזת JSON */
    private fun vehicleLookup(cb: String, plate: String) {
        val d = plate.filter { it.isDigit() }
        if (d.length != 7 && d.length != 8) { reject(cb, "bad_plate"); return }
        var con: HttpURLConnection? = null
        try {
            val filters = URLEncoder.encode("{\"mispar_rechev\":\"$d\"}", "UTF-8")
            val url = URL("https://data.gov.il/api/3/action/datastore_search" +
                "?resource_id=053cea08-09bc-40ec-8f7a-156f0677aff3&limit=1&filters=$filters")
            con = (url.openConnection() as HttpURLConnection).apply {
                connectTimeout = 10000
                readTimeout = 15000
                setRequestProperty("User-Agent", userAgent)
                setRequestProperty("Accept", "application/json")
            }
            val code = con.responseCode
            if (code !in 200..299) { reject(cb, "http_$code"); return }
            val body = con.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
            val literal = JSONObject.quote(body).replace("\u2028", "\\u2028").replace("\u2029", "\\u2029")
            resolve(cb, literal)
        } catch (e: Exception) {
            reject(cb, "network")
        } finally {
            con?.disconnect()
        }
    }

    // ---------- the JavaScript bridge ----------

    inner class Bridge {
        private val idRe = Regex("^[A-Za-z0-9-]{1,64}$")

        @JavascriptInterface fun version(): String = "1"

        @JavascriptInterface fun hasDictation(): Boolean = SpeechRecognizer.isRecognitionAvailable(this@MainActivity)

        /** שומר תמונה (base64 של JPEG) באחסון הפנימי ומחזיר מזהה. מוגשת בכתובת /blobs/<id>.jpg */
        @JavascriptInterface fun saveBlob(b64: String): String {
            val id = UUID.randomUUID().toString()
            File(blobDir, "$id.jpg").writeBytes(Base64.decode(b64, Base64.DEFAULT))
            return id
        }

        @JavascriptInterface fun deleteBlob(id: String) {
            if (idRe.matches(id)) File(blobDir, "$id.jpg").delete()
        }

        @JavascriptInterface fun readPlate(cb: String) = runOnUiThread { startPlate(cb) }

        @JavascriptInterface fun getLocation(cb: String) = runOnUiThread { startLocation(cb) }

        @JavascriptInterface fun startDictation() = runOnUiThread { this@MainActivity.startDictation() }

        @JavascriptInterface fun stopDictation() = runOnUiThread { speech?.stopListening() }

        @JavascriptInterface fun saveFile(cb: String, name: String, mime: String, content: String) = io.execute {
            try {
                val where = saveToDownloads(name, mime, content.toByteArray(Charsets.UTF_8))
                resolve(cb, JSONObject().put("status", "saved").put("where", where).toString())
            } catch (e: Exception) { reject(cb, "save_failed") }
        }

        @JavascriptInterface fun vehicleLookup(cb: String, plate: String) = net.execute {
            this@MainActivity.vehicleLookup(cb, plate)
        }

        @JavascriptInterface fun shareFile(name: String, mime: String, content: String) = io.execute {
            try { share(name, mime, content.toByteArray(Charsets.UTF_8)) } catch (_: Exception) {}
        }
    }
}
