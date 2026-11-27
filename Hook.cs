// Esp2JpnHook - Conversor de letra española a fonemas X-SAMPA de bancos japoneses clásicos.
// v3: la conversión es el g2p de Esp2XSAMPA.py (v3.0.1), portado a C# sin cambios de reglas.
//     (la tabla anterior, basada en Esp2Jpn.lua, ya no se usa).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Esp2Jpn
{
    public static class Hook
    {
        // ------------------------------------------------------------------
        // API usada por el parche IL: devuelve fonemas o null (= no tocar).
        // ------------------------------------------------------------------
        public static string Convert(string lyric, int langID)
        {
            string res = null, why = "";
            try
            {
                LoadConfig();
                if (!_enabled) why = "desactivado";
                else if (!LangAllowed(langID)) why = "idioma no permitido";
                else if (lyric == null || lyric.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0) why = "vacio o varias palabras";
                else { res = LyricToJapanesePhonemes(lyric); if (res == null) why = "no es texto latino"; }
            }
            catch (Exception e) { why = "excepcion " + e.Message; res = null; }
            Log("lyric='" + lyric + "' langID=" + langID + " -> " + (res ?? "(nada: " + why + ")"));
            return res;                                   // null = el editor sigue como siempre
        }

        // ------------------------------------------------------------------
        // Guarda "no repartir": se llama ANTES de que el motor nativo procese la letra.
        // Si devuelve distinto de null, el parche anula el parámetro nextNote de
        // SetLyricsAndPhonemesByAutoPriority / ByLangID: así el motor no puede pasar sílabas
        // ni fonemas a la nota siguiente, y todos los fonemas quedan en la nota actual.
        // (nosplit=0 en Esp2Jpn.ini lo desactiva.)
        // ------------------------------------------------------------------
        public static string ConvertPre(string lyric, int langID)
        {
            try
            {
                LoadConfig();
                if (!_noSplit) return null;
                return Convert(lyric, langID);
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------
        // v4: REPARACIÓN DEL REPARTO DE LETRA.
        // El motor nativo puede recortar la letra de la nota editada a su primera sílaba ("ho") y
        // pasar el resto ("laquetal") a la nota siguiente, aunque los fonemas ya estén bien.
        // El parche llama a Begin() al ENTRAR y a End() al SALIR de cada capa (extensión, AutoPriority,
        // ByLangID). Begin guarda el estado de la nota siguiente; End (solo en la capa más externa),
        // si la letra se convierte con este parche, deja la letra COMPLETA en la nota editada y devuelve
        // la nota siguiente a su estado anterior (letra, fonemas y candado).
        // ------------------------------------------------------------------
        [ThreadStatic] static int _depth;
        [ThreadStatic] static DateTime _lastBegin;
        [ThreadStatic] static bool _repairing;
        [ThreadStatic] static object _snapNote;
        [ThreadStatic] static List<NoteSnap> _snaps;
        const int MAX_FOLLOWING = 8;                       // cuántas notas siguientes se vigilan (una palabra)
        const int MAX_WATCH = 4000;                        // tope de notas vigiladas con varias palabras
        static readonly char[] WS = { ' ', '\t', '\r', '\n' };

        static string Cut(string s)
        {
            if (s == null) return "(null)";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n");
            return s.Length > 60 ? s.Substring(0, 60) + "..." : s;
        }

        // Diagnóstico (solo con log=1): quién llamó, para saber por qué vía llega "Insert Lyrics".
        static string Caller()
        {
            if (!_log) return "";
            try
            {
                System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(false);
                StringBuilder sb = new StringBuilder();
                int shown = 0;
                for (int i = 0; i < st.FrameCount && shown < 14; i++)
                {
                    MethodBase m = st.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null) continue;
                    if (m.DeclaringType.FullName == "Esp2Jpn.Hook" || m.DeclaringType.FullName == "Esp2Jpn.Bridge") continue;
                    if (m.DeclaringType.FullName.StartsWith("System.")) continue;
                    if (shown > 0) sb.Append(" <- ");
                    sb.Append(m.DeclaringType.Name + "." + m.Name);
                    shown++;
                }
                return sb.ToString();
            }
            catch (Exception) { return "(sin pila)"; }
        }

        sealed class NoteSnap { public object Note; public string Lyric, Ph; public object Prot; public int Lang; }

        static void ClearSnap() { _snapNote = null; _snaps = null; }

        // El motor nativo reparte la letra entre las notas SIGUIENTES (WIVSMNote.Next), no a través del
        // parámetro nextNote (que llega null). Por eso se guarda el estado de la cadena Next/Next/...
        public static string Begin(object note, object next, string lyrics)
        {
            try
            {
                // si una llamada anterior terminó con excepción, el contador queda sucio: se reinicia por tiempo
                if (_depth > 0 && (DateTime.UtcNow - _lastBegin).TotalSeconds > 5) { _depth = 0; ClearSnap(); }
                _lastBegin = DateTime.UtcNow;
                if (_repairing) return null;
                LoadConfig();
                _depth++;
                Log("Begin depth=" + _depth + " lyrics='" + Cut(lyrics) + "'" + (_depth == 1 ? "  pila: " + Caller() : ""));
                if (_depth != 1 || note == null) return null;
                ClearSnap(); _snapNote = note;
                // v4.5: una letra con varias palabras puede repartirse por MUCHAS notas siguientes: se vigilan todas las posibles
                int max = MAX_FOLLOWING;
                if (lyrics != null && lyrics.IndexOfAny(WS) >= 0) max = Math.Min(Math.Max(MAX_FOLLOWING, lyrics.Length), MAX_WATCH);
                List<NoteSnap> l = new List<NoteSnap>();
                object n = GetProp(note, "Next");
                while (n != null && l.Count < max)
                {
                    NoteSnap sn = new NoteSnap();
                    sn.Note = n; sn.Lyric = GetProp(n, "Lyric") as string; sn.Ph = GetProp(n, "Phonemes") as string;
                    sn.Prot = GetProp(n, "IsProtected");
                    try { sn.Lang = System.Convert.ToInt32(GetProp(n, "LangID")); } catch { sn.Lang = 0; }
                    l.Add(sn);
                    n = GetProp(n, "Next");
                }
                _snaps = l;
                Log("Begin: notas siguientes guardadas=" + l.Count);
            }
            catch (Exception e) { Log("Begin excepcion " + e.Message); _depth = 0; ClearSnap(); }
            return null;
        }

        public static string End(object note, string lyrics)
        {
            try
            {
                if (_repairing) return null;
                if (_depth > 0) _depth--;
                if (_depth > 0) return null;                          // no es la capa más externa
                List<NoteSnap> snaps = _snaps;
                bool same = ReferenceEquals(note, _snapNote);
                ClearSnap();
                if (note == null || lyrics == null || !same) return null;
                LoadConfig();
                // v4.5: varias palabras separadas por espacio -> UNA palabra por nota (como en un idioma latino);
                // el motor japonés las habría troceado en moras e ignorado los espacios.
                if (_enabled && _words && lyrics.IndexOfAny(WS) >= 0)
                {
                    _repairing = true;
                    try { DistributeWords(note, lyrics, snaps); }
                    finally { _repairing = false; }
                    return null;
                }
                if (!_enabled || !_noSplit) return null;
                int id = LangOf(note);
                string ph = Convert(lyrics, id);                      // null = esta letra no la gestiona el parche
                if (ph == null) return null;

                _repairing = true;
                try
                {
                    bool fixedAny = false;
                    string cur = GetProp(note, "Lyric") as string;
                    // la letra de la nota quedó recortada (p. ej. "ho" en vez de "holaquetal")
                    if (cur != null && cur != lyrics && cur.Length < lyrics.Length &&
                        lyrics.StartsWith(cur, StringComparison.OrdinalIgnoreCase))
                    { SetProp(note, "Lyric", lyrics); fixedAny = true; }
                    // las notas siguientes que el reparto haya modificado vuelven a su estado anterior
                    if (snaps != null)
                        foreach (NoteSnap sn in snaps)
                            if (RestoreNext(sn.Note, sn.Lyric, sn.Ph, sn.Prot, sn.Lang)) { fixedAny = true; Log("End: nota siguiente restaurada a '" + sn.Lyric + "'"); }
                    if (fixedAny)
                    {
                        SetProp(note, "IsProtected", false);          // una nota protegida ignora SetPhonemes
                        ApplyPhonemes(note, ph, id);                  // fonemas completos + candado
                    }
                    Log("End: lyrics='" + lyrics + "' reparado=" + fixedAny);
                }
                finally { _repairing = false; }
            }
            catch (Exception e) { Log("End excepcion " + e.Message); _depth = 0; ClearSnap(); _repairing = false; }
            return null;
        }

        static int LangOf(object note)
        {
            try { return System.Convert.ToInt32(GetProp(note, "LangID")); } catch { return -1; }
        }

        // Texto latino con varias palabras -> lista de palabras; null si no se debe tocar
        // (kana, [x-sampa], menos de 2 palabras...).
        static List<string> WordTokens(string lyrics)
        {
            if (lyrics == null) return null;
            foreach (char c in lyrics)
                if (!LetraValida(c) && c != '\t' && c != '\r' && c != '\n') return null;
            string[] parts = lyrics.Split(WS, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            return new List<string>(parts);
        }

        // Reparte "En un dia ..." : palabra i -> nota i (la editada y sus siguientes), cada una con sus fonemas
        // completos y el candado. Las notas que el motor ensució más allá de la última palabra vuelven a su estado anterior.
        static void DistributeWords(object note, string lyrics, List<NoteSnap> snaps)
        {
            List<string> words = WordTokens(lyrics);
            if (words == null) { Log("Distribute: texto no latino o menos de 2 palabras, no se toca"); return; }
            if (!LangAllowed(LangOf(note))) { Log("Distribute: idioma de la nota no permitido"); return; }

            List<object> targets = new List<object>();
            targets.Add(note);
            object n = GetProp(note, "Next");
            while (n != null && targets.Count < words.Count) { targets.Add(n); n = GetProp(n, "Next"); }

            for (int i = 0; i < targets.Count; i++) SetWord(targets[i], words[i]);

            int restored = 0;
            if (snaps != null)
                foreach (NoteSnap sn in snaps)
                {
                    if (targets.IndexOf(sn.Note) >= 0) continue;
                    if (RestoreNext(sn.Note, sn.Lyric, sn.Ph, sn.Prot, sn.Lang)) restored++;
                }
            Log("Distribute: palabras=" + words.Count + " notas=" + targets.Count + " restauradas=" + restored +
                (words.Count > targets.Count ? " (faltan " + (words.Count - targets.Count) + " notas)" : ""));
        }

        static void SetWord(object n, string word)
        {
            int id = LangOf(n);
            string ph = LangAllowed(id) ? LyricToJapanesePhonemes(word) : null;
            SetProp(n, "IsProtected", false);                       // una nota protegida ignora SetPhonemes
            if ((GetProp(n, "Lyric") as string) != word) SetProp(n, "Lyric", word);
            if (ph != null) ApplyPhonemes(n, ph, id);               // fonemas completos + candado
            Log("Distribute: '" + word + "' -> " + (ph ?? "(sin conversión)"));
        }

        // Devuelve true si la nota había cambiado y se restauró.
        static bool RestoreNext(object next, string lyr, string ph, object prot, int lang)
        {
            string nowLyr = GetProp(next, "Lyric") as string;
            string nowPh = GetProp(next, "Phonemes") as string;
            bool lyrChanged = nowLyr != lyr;
            bool phChanged = ph != null && NormPh(nowPh ?? "") != NormPh(ph);
            if (!lyrChanged && !phChanged) return false;
            SetProp(next, "IsProtected", false);
            if (lyrChanged && lyr != null) SetProp(next, "Lyric", lyr);
            string now2 = GetProp(next, "Phonemes") as string;
            if (ph != null && ph.Length > 0 && NormPh(now2 ?? "") != NormPh(ph)) ApplyPhonemes(next, ph, lang);
            SetProp(next, "IsProtected", (prot is bool) ? prot : (object)false);
            return true;
        }

        static void SetProp(object o, string name, object value)
        {
            if (o == null) return;
            PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null || !p.CanWrite || p.GetIndexParameters().Length > 0) return;
            p.SetValue(o, value, null);
        }

        // ------------------------------------------------------------------
        // Reconversión de notas existentes (cambio de banco de voz / normalización).
        // Se llama desde el puente con una pista o parte (objeto con propiedad Notes).
        // Solo toca notas NO protegidas, de idioma permitido y con letra latina de una palabra.
        // Todo por reflexión (este ensamblado no referencia VOCALOID7.dll).
        // ------------------------------------------------------------------
        static bool _busy;

        public static string ConvertNotes(object owner)
        {
            if (owner == null || _busy) return null;
            _busy = true;
            try
            {
                LoadConfig();
                if (!_enabled) return null;
                object notes = GetProp(owner, "Notes");
                System.Collections.IEnumerable en = notes as System.Collections.IEnumerable;
                if (en == null) { Log("ConvertNotes(" + owner.GetType().Name + "): sin lista Notes (" + (notes == null ? "null" : notes.GetType().FullName) + ")"); return null; }
                List<object> list = new List<object>();
                foreach (object n in en) list.Add(n);
                int total = 0, done = 0, prot = 0, lang = 0, skip = 0, fail = 0, same = 0;
                string seen = "";
                foreach (object n in list)
                {
                    total++;
                    string lyr = GetProp(n, "Lyric") as string;
                    int id = -1;
                    try { id = System.Convert.ToInt32(GetProp(n, "LangID")); } catch { }
                    if (seen.IndexOf("," + id + ",") < 0) seen += "," + id + ",";
                    if (lyr == null) { skip++; continue; }
                    object pr = GetProp(n, "IsProtected");
                    if (pr is bool && (bool)pr) { prot++; continue; }
                    if (!LangAllowed(id)) { lang++; continue; }
                    if (lyr.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0) { skip++; continue; }
                    string ph = LyricToJapanesePhonemes(lyr);
                    if (ph == null) { skip++; continue; }
                    // ya tiene exactamente estos fonemas (p. ej. el usuario acaba de quitar el candado): no tocar ni volver a proteger
                    string cur = GetProp(n, "Phonemes") as string;
                    if (cur != null && NormPh(cur) == NormPh(ph)) { same++; continue; }
                    if (ApplyPhonemes(n, ph, id)) done++; else fail++;
                }
                Log("ConvertNotes(" + owner.GetType().Name + "): notas=" + total + " convertidas=" + done + " protegidas=" + prot + " ya-iguales=" + same +
                    " otro-idioma=" + lang + " no-latinas/otras=" + skip + " fallos=" + fail + " idiomas vistos=" + seen.Trim(','));
            }
            catch (Exception e) { Log("ConvertNotes excepcion " + e); }
            finally { _busy = false; }
            return null;
        }

        static string NormPh(string p)
        {
            return string.Join(" ", p.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        static object GetProp(object o, string name)
        {
            if (o == null) return null;
            PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null || !p.CanRead || p.GetIndexParameters().Length > 0) return null;
            return p.GetValue(o, null);
        }

        static bool ApplyPhonemes(object note, string ph, int langID)
        {
            Type t = note.GetType();
            MethodInfo mi = null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.Name == "SetPhonemes" && m.GetParameters().Length == 3) { mi = m; break; }
            if (mi == null) { Log("ApplyPhonemes: no hay SetPhonemes(3 parametros) en " + t.FullName); return false; }
            ParameterInfo[] ps = mi.GetParameters();
            object[] a = new object[3];
            for (int i = 0; i < 3; i++)
            {
                Type pt = ps[i].ParameterType;
                if (pt == typeof(string)) a[i] = ph;
                else if (pt == typeof(bool)) a[i] = true;
                else if (pt.IsEnum) a[i] = Enum.ToObject(pt, langID);
                else a[i] = System.Convert.ChangeType(langID, pt);
            }
            mi.Invoke(note, a);
            PropertyInfo pp = t.GetProperty("IsProtected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pp != null && pp.CanWrite) pp.SetValue(note, true, null);
            return true;
        }

        // ------------------------------------------------------------------
        // Configuración: Esp2Jpn.ini junto a la DLL
        //   enabled=1|0     activa/desactiva
        //   langs=0         idiomas de nota a convertir (0 = japonés). "*" = todos
        //   log=1|0         escribe Esp2Jpn.log (diagnóstico)
        //   nosplit=1|0     1 = todos los fonemas en la nota donde se escribe la letra (no repartir)
        //   words=1|0       1 = varias palabras separadas por espacio (Insert Lyrics) -> una palabra por nota
        //   case=1|0        1 = las mayúsculas U I J G R C Y Z A E O tienen el significado de Esp2XSAMPA
        //   ll=Z|S|j        sonido de "ll" y de "y" consonántica (zheísmo / sheísmo / yeísmo)
        //   zeta=sdz|sts|sz esquema de z y C (minúscula/MAYÚSCULA): sdz = s/dz (predeterminado), sts = s/ts, sz = s/z
        // ------------------------------------------------------------------
        static DateTime _stamp; static bool _enabled = true, _log = false, _anyLang = false, _noSplit = true, _caseMarks = true, _words = true;
        static string _yeismo = "Z", _zeta = "sdz";
        static List<int> _langs = new List<int> { 0 }; static string _dir;

        static string Dir()
        {
            if (_dir == null)
            {
                try { _dir = Path.GetDirectoryName(typeof(Hook).Assembly.Location); } catch { }
                if (string.IsNullOrEmpty(_dir)) _dir = AppDomain.CurrentDomain.BaseDirectory;
            }
            return _dir;
        }

        static void LoadConfig()
        {
            string ini = Path.Combine(Dir(), "Esp2Jpn.ini");
            if (!File.Exists(ini)) return;
            DateTime t = File.GetLastWriteTimeUtc(ini);
            if (t == _stamp) return;
            _stamp = t; _enabled = true; _log = false; _anyLang = false; _langs = new List<int> { 0 };
            _noSplit = true; _caseMarks = true; _words = true; _yeismo = "Z"; _zeta = "sdz";
            foreach (string l in File.ReadAllLines(ini))
            {
                string s = l.Trim(); if (s.StartsWith(";") || s.IndexOf('=') < 0) continue;
                string k = s.Substring(0, s.IndexOf('=')).Trim().ToLowerInvariant();
                string v = s.Substring(s.IndexOf('=') + 1).Trim().ToLowerInvariant();
                if (k == "enabled") _enabled = !(v == "0" || v == "false" || v == "no");
                else if (k == "log") _log = (v == "1" || v == "true" || v == "yes" || v == "si");
                else if (k == "nosplit") _noSplit = !(v == "0" || v == "false" || v == "no");
                else if (k == "case") _caseMarks = !(v == "0" || v == "false" || v == "no");
                else if (k == "words") _words = !(v == "0" || v == "false" || v == "no");
                else if (k == "ll") { if (v == "z") _yeismo = "Z"; else if (v == "s") _yeismo = "S"; else if (v == "j") _yeismo = "j"; }
                else if (k == "zeta") { if (v == "sdz" || v == "sts" || v == "sz") _zeta = v; }
                else if (k == "langs")
                {
                    if (v == "*") _anyLang = true;
                    else
                    {
                        _langs = new List<int>();
                        foreach (string p in v.Split(','))
                        { int n; if (int.TryParse(p.Trim(), out n)) _langs.Add(n); }
                    }
                }
            }
        }

        static bool LangAllowed(int id) { return _anyLang || _langs.Contains(id); }

        // Solo para la vista previa del parcheador: fija el sonido de ll / y (Z, S o j) sin leer Esp2Jpn.ini.
        // (Dentro de VOCALOID7 el valor sale de ll= en Esp2Jpn.ini.)
        public static void SetYeismo(string v)
        {
            if (v == "Z" || v == "S" || v == "j") _yeismo = v;
        }

        // Solo para la vista previa del parcheador: fija el esquema de z y C (sdz, sts o sz) sin leer Esp2Jpn.ini.
        // (Dentro de VOCALOID7 el valor sale de zeta= en Esp2Jpn.ini.)
        public static void SetZeta(string v)
        {
            if (v == "sdz" || v == "sts" || v == "sz") _zeta = v;
        }

        // Fonema de la Z mayúscula y de la C mayúscula ante e / i, según el esquema (zeta=): dz, ts o z.
        static string ZetaCap() { return _zeta == "sts" ? "ts" : _zeta == "sz" ? "z" : "dz"; }

        public static string LogDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Esp2Jpn");
        }

        // Sin registros por defecto. Con log=1 en Esp2Jpn.ini se escribe %LOCALAPPDATA%\Esp2Jpn\Esp2Jpn.log (diagnóstico).
        static void Log(string m)
        {
            if (!_log) return;
            try
            {
                string d = LogDir(); Directory.CreateDirectory(d);
                File.AppendAllText(Path.Combine(d, "Esp2Jpn.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + m + "\r\n");
            }
            catch { }
        }

        // ==================================================================
        //  Español -> X-SAMPA para VOCALOID japonés   (port de Esp2XSAMPA.py v3.0.1)
        // ==================================================================
        // Fonemas que produce (los únicos que acepta una voz japonesa):
        //   vocales:      a i M e o
        //   consonantes:  k g s S z dz Z dZ t ts tS d n J h C p\ p b m j 4 w N
        //   palatalizadas (solo ante i):  k' g' N' t' d' p' b' m' 4' p\'
        // Mayúsculas con significado (igual que Bloq Mayús/Shift en el script):
        //   U = M siempre | I = i siempre (nunca j) | J (y G de gi) = C ante i, p\ M ante u
        //   R = rr (4 4), RR = 4 4 4 4  | Z = variante de z       | A E O (última vocal) tras C+i: absorbe la i
        //   Y ante vocal (inicio de sílaba) = j siempre, en todos los modos de ll / y
        //   C ante e / i (Ce, Ci) cambia igual que la Z mayúscula, según zeta=: dz, ts o z  (Ca, Co, Cu y Ch no cambian)
        // Minúsculas: sh = S siempre (sha -> S a), igual que ch = tS.
        const char U_CAP = '\u0003', DIERESIS_CAP = '\u0004', J_CAP = '\u0005', G_CAP = '\u0006',
                   R_CAP = '\u0007', I_CAP = '\u000e', Z_CAP = '\u0010', Y_CAP = '\u0011', C_CAP = '\u0013', I_MARCA = '\u0012';
        const string IM = "\u0012";
        const string R_L_JAPONESA = "4", JOTA = "h", JOTA_FUERTE = "C", VOCAL_U = "M", F_SUAVE = "p\\";
        const string SUAVES = "eiI\u000e\u0012";                       // vocales que suavizan c y g
        const string VOCALES = "aeiouIU\u0003\u0004\u000e";            // I y U = i, u acentuadas
        const string FUERTES = "aeo";
        const string CONS_ANTES_I = "bcdfgjklmnñprstvwxzBCDFGJKLMNÑPRSTVWXZ";
        const string TONICA_FIN = "áéóúAEOUÁÉÓÚ";
        static readonly Regex RE_I_TONICA = new Regex("([" + CONS_ANTES_I + "])i([aeouáéóúAEOUÁÉÓÚ]+)");

        static readonly Dictionary<string, string> PALATAL = new Dictionary<string, string>
        {
            {"k","k'"},{"g","g'"},{"N","N'"},{"t","t'"},{"d","d'"},{"p","p'"},
            {"b","b'"},{"m","m'"},{"4","4'"},{"p\\","p\\'"}
        };
        static readonly Dictionary<string, string> ANTE_I = new Dictionary<string, string> { {"n","J"} };

        // true si c (no vacío) está en el conjunto. '\0' representa "no hay carácter".
        static bool In(string set, char c) { return c != '\0' && set.IndexOf(c) >= 0; }

        // minúsculas y sin tildes, conserva ñ y ü; í/ú pasan a I/U (acentuadas: nunca diptongo)
        static string Normalizar(string palabra)
        {
            string p = palabra.ToLowerInvariant();
            p = p.Replace('\u00f1', '\u0001').Replace('\u00fc', '\u0002');
            p = p.Replace('\u00ed', 'I').Replace('\u00fa', 'U');
            p = p.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(p.Length);
            foreach (char c in p)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Replace('\u0001', '\u00f1').Replace('\u0002', '\u00fc');
        }

        static string Jota(bool fuerte, char sig)
        {
            return (fuerte && (sig == 'i' || sig == 'I' || sig == I_CAP)) ? JOTA_FUERTE : JOTA;
        }

        // Una palabra/sílaba -> fonemas X-SAMPA separados por espacios (todos juntos, para UNA nota).
        public static string G2P(string palabra)
        {
            palabra = RE_I_TONICA.Replace(palabra, delegate (Match m)
            {
                string g = m.Groups[2].Value;
                return m.Groups[1].Value + (TONICA_FIN.IndexOf(g[g.Length - 1]) >= 0 ? IM : "i") + g;
            });
            palabra = palabra.Replace('U', U_CAP).Replace('\u00dc', DIERESIS_CAP);
            palabra = palabra.Replace('J', J_CAP).Replace('G', G_CAP).Replace('R', R_CAP);
            palabra = palabra.Replace('Z', Z_CAP).Replace('Y', Y_CAP).Replace('C', C_CAP);
            palabra = palabra.Replace('I', I_CAP);            // antes de normalizar (que pasa a minúsculas)
            string w = Normalizar(palabra);
            int n = w.Length;
            List<string> o = new List<string>();
            int i = 0;
            while (i < n)
            {
                char c = w[i];
                char ant = i > 0 ? w[i - 1] : '\0';
                char sig = i + 1 < n ? w[i + 1] : '\0';
                char sig2 = i + 2 < n ? w[i + 2] : '\0';
                bool suave = In(SUAVES, sig);

                if (c == 'c' || c == C_CAP)
                {
                    if (sig == 'h') { o.Add("tS"); i += 2; continue; }
                    if (c == C_CAP && suave) o.Add(ZetaCap());               // Ce, Ci con C mayúscula: igual que la Z mayúscula
                    else o.Add(suave ? "s" : "k");
                }
                else if (c == 'q')
                {
                    o.Add("k");
                    if (sig == 'u' || sig == U_CAP) i++;                 // la u de que/qui es muda
                }
                else if (c == 'g' || c == G_CAP)
                {
                    if ((sig == 'u' || sig == U_CAP) && In(SUAVES, sig2)) { o.Add("g"); i++; }   // gue, gui
                    else if (suave) o.Add(Jota(c == G_CAP, sig));
                    else o.Add("g");
                }
                else if (c == 'l')
                {
                    if (sig == 'l') { o.Add(_yeismo); i += 2; continue; }
                    o.Add(R_L_JAPONESA);
                }
                else if (c == 'r' || c == R_CAP)
                {
                    bool doble = sig == 'r' || sig == R_CAP;
                    if (c == R_CAP && sig == R_CAP)                       // RR -> erre fuertemente pronunciada: 4 4 4 4
                    { for (int q = 0; q < 4; q++) o.Add(R_L_JAPONESA); i += 2; continue; }
                    if (doble || c == R_CAP) { o.Add(R_L_JAPONESA); o.Add(R_L_JAPONESA); i += doble ? 2 : 1; continue; }
                    o.Add(R_L_JAPONESA);
                }
                else if (c == Y_CAP && sig != '\0' && In(VOCALES, sig))
                    o.Add("j");                                          // Y en mayúscula al inicio de sílaba: siempre j
                else if (c == 'y' || c == Y_CAP)
                {
                    if (sig == '\0' || !In(VOCALES, sig))
                    {
                        if (n == 1) o.Add("i");
                        else o.Add(In(VOCALES, ant) ? "j" : "i");
                    }
                    else o.Add(_yeismo);
                }
                else if (c == 'i')
                {
                    bool glide = In(FUERTES, sig) || In(FUERTES, ant) || sig == 'u' || sig == U_CAP;
                    o.Add(glide ? "j" : "i");
                }
                else if (c == I_MARCA) o.Add(IM);                        // se resuelve después
                else if (c == 'u')
                {
                    bool glide = In(FUERTES, sig) || In(FUERTES, ant) || sig == 'i';
                    o.Add(glide ? "w" : VOCAL_U);
                }
                else if (c == '\u00fc') o.Add("w");
                else if (c == U_CAP || c == DIERESIS_CAP) o.Add(VOCAL_U);
                else if (c == 'I' || c == I_CAP) o.Add("i");
                else if (c == 'U') o.Add(VOCAL_U);
                else if (c == '\u00f1') o.Add("J");
                else if (c == 'h') { }                                   // muda
                else if (c == 'j' || c == J_CAP)
                {
                    bool fuerte = c == J_CAP;
                    if (fuerte)
                    {
                        bool glideU = sig == 'u' && sig2 != '\0' && (In(FUERTES, sig2) || sig2 == 'i');
                        if (sig != '\0' && (sig == 'u' || sig == 'U' || sig == U_CAP) && !glideU)
                        { o.Add("p\\"); o.Add(VOCAL_U); i += 2; continue; }   // JU -> fu
                    }
                    o.Add(Jota(fuerte, sig));
                }
                else if (c == 's')
                {
                    if (sig == 'h') { o.Add("S"); i += 2; continue; }       // sh: siempre S (sha -> S a)
                    o.Add("s");
                }
                else if (c == 'z') o.Add("s");
                else if (c == Z_CAP) o.Add(ZetaCap());                   // Z en mayúscula
                else if (c == 'v') o.Add("b");
                else if (c == 'f') o.Add(F_SUAVE);
                else if (c == 'x') { o.Add("k"); o.Add("s"); }
                else o.Add(c.ToString());
                i++;
            }
            // consonante + i + vocal tónica -> C' vocal  (la i se absorbe en la consonante palatalizada)
            for (int k = 1; k < o.Count; k++)
            {
                if (o[k] != IM) continue;
                string prev = o[k - 1], rep;
                if (prev != null && prev != "p\\" && PALATAL.TryGetValue(prev, out rep)) { o[k - 1] = rep; o[k] = null; }
                else if (prev != null && ANTE_I.TryGetValue(prev, out rep)) { o[k - 1] = rep; o[k] = null; }
                else o[k] = "j";                                         // sin versión palatal: diptongo normal
            }
            o.RemoveAll(delegate (string x) { return x == null; });
            // consonante + i -> versión palatal válida
            for (int k = 0; k < o.Count - 1; k++)
            {
                if (o[k + 1] != "i") continue;
                string rep2;
                if (PALATAL.TryGetValue(o[k], out rep2)) o[k] = rep2;
                else if (ANTE_I.TryGetValue(o[k], out rep2)) o[k] = rep2;
            }
            return string.Join(" ", o.ToArray());
        }

        // Solo conservan su mayúscula las letras que el script marca (u ü i j g r z y c a e o);
        // el resto se pasa a minúscula, como hace el filtro de teclado de Esp2XSAMPA.
        static string AplicarMayusculas(string text)
        {
            if (!_caseMarks) return text.ToLowerInvariant();
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append("uüijgrzycaeoUÜIJGRZYCAEO".IndexOf(c) >= 0 ? c : char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static bool LetraValida(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || "áéíóúüñÁÉÍÓÚÜÑ' -".IndexOf(c) >= 0;
        }

        // ------------------------------------------------------------------
        // Letra -> fonemas. Toda la palabra/sílaba va a UNA sola nota: los separadores
        // internos (guion, apóstrofe, espacio en la vista previa) solo unen fonemas.
        // Devuelve null si el texto no parece español en alfabeto latino.
        // ------------------------------------------------------------------
        public static string LyricToJapanesePhonemes(string lyric)
        {
            if (string.IsNullOrEmpty(lyric)) return null;
            string text = lyric.Trim();
            if (text.Length == 0) return null;
            // "-" = prolongar la nota anterior (bancos de voz como Maika): el motor japonés lo interpreta
            // como extensión de la vocal anterior.
            if (text == "-") return "-";
            foreach (char c in text)
                if (!LetraValida(c)) return null;                   // kana, [x-sampa], etc.
            text = AplicarMayusculas(text);

            List<string> all = new List<string>();
            foreach (string w in text.Split(new[] { ' ', '-', '\'' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string ph = G2P(w);
                if (ph.Length > 0) all.Add(ph);
            }
            if (all.Count == 0) return null;                        // p. ej. "h" sola
            return string.Join(" ", all.ToArray());
        }

        // ------------------------------------------------------------------
        // Autotest: compara con los resultados del script Python original (Esp2XSAMPA.py v3.0.1)
        // con la configuración por defecto (ll=Z, zeta=sdz, case=1).
        //   Esp2JpnPatcher.exe --selftest
        // ------------------------------------------------------------------
        static readonly string[,] SelfTestVectors = new string[,]
        {
            { "hola", "o 4 a" },
            { "cantando", "k a n t a n d o" },
            { "cielo", "s j e 4 o" },
            { "ñu", "J M" },
            { "rico", "4' i k o" },
            { "tiene", "t j e n e" },
            { "baile", "b a j 4 e" },
            { "baIle", "b a i 4 e" },
            { "tIene", "t' i e n e" },
            { "hay", "a j" },
            { "cuando", "k w a n d o" },
            { "CUANDO", "k M a n d o" },
            { "pió", "p' o" },
            { "kiú", "k' M" },
            { "niO", "J o" },
            { "piO", "p' o" },
            { "kiU", "k' M" },
            { "ja", "h a" },
            { "JA", "h a" },
            { "jin", "h i n" },
            { "JIN", "C i n" },
            { "jun", "h M n" },
            { "JUN", "p\\ M n" },
            { "gente", "h e n t e" },
            { "Gente", "h e n t e" },
            { "GIN", "C i n" },
            { "guerra", "g e 4 4 a" },
            { "quiero", "k j e 4 o" },
            { "que", "k e" },
            { "zapato", "s a p a t o" },
            { "Zapato", "dz a p a t o" },
            { "mismo", "m' i s m o" },
            { "ZA", "dz a" },
            { "llave", "Z a b e" },
            { "yo", "Z o" },
            { "ay", "a j" },
            { "hoy", "o j" },
            { "y", "i" },
            { "ro", "4 o" },
            { "RO", "4 4 o" },
            { "mar", "m a 4" },
            { "maR", "m a 4 4" },
            { "perro", "p e 4 4 o" },
            { "fiesta", "p\\ j e s t a" },
            { "fi", "p\\' i" },
            { "ni", "J i" },
            { "ti", "t' i" },
            { "di", "d' i" },
            { "bi", "b' i" },
            { "mi", "m' i" },
            { "ri", "4' i" },
            { "Ri", "4 4' i" },
            { "chico", "tS i k o" },
            { "extra", "e k s t 4 a" },
            { "xilofono", "k s i 4 o p\\ o n o" },
            { "paraguas", "p a 4 a g w a s" },
            { "pingüino", "p' i n g w i n o" },
            { "aire", "a j 4 e" },
            { "causa", "k a w s a" },
            { "ciudad", "s j M d a d" },
            { "cuidado", "k w i d a d o" },
            { "Idea", "i d e a" },
            { "universidad", "M J i b e 4 s i d a d" },
            { "corazón", "k o 4 a s o n" },
            { "canción", "k a n s j o n" },
            { "mañana", "m a J a n a" },
            { "amor", "a m o 4" },
            { "reina", "4 e j n a" },
            { "vivir", "b' i b' i 4" },
            { "jamás", "h a m a s" },
            { "lluvia", "Z M b j a" },
            { "Yo", "j o" },
            { "Ya", "j a" },
            { "YO", "j o" },
            { "maYa", "m a j a" },
            { "aYUda", "a j M d a" },
            { "Yuvia", "j M b j a" },
            { "hoY", "o j" },
            { "reY", "4 e j" },
            { "sha", "S a" },
            { "SHA", "S a" },
            { "shi", "S i" },
            { "shu", "S M" },
            { "Sha", "S a" },
            { "sa", "s a" },
            { "sol", "s o 4" },
            { "ash", "a S" },
            { "cha", "tS a" },
            { "ce", "s e" },
            { "Ce", "dz e" },
            { "CE", "dz e" },
            { "ci", "s i" },
            { "Ci", "dz i" },
            { "Cé", "dz e" },
            { "Cí", "dz i" },
            { "Cielo", "dz j e 4 o" },
            { "cena", "s e n a" },
            { "Cena", "dz e n a" },
            { "haCer", "a dz e 4" },
            { "Ca", "k a" },
            { "Co", "k o" },
            { "Cha", "tS a" },
            { "UnA", "M n a" },
            { "güe", "g w e" },
            { "buey", "b w e j" },
            { "muy", "m M j" },
            { "Rosa", "4 4 o s a" },
            { "RR", "4 4 4 4" },
            { "peRRo", "p e 4 4 4 4 o" },
            { "RRo", "4 4 4 4 o" },
            { "RRi", "4 4 4 4' i" },
            { "Rr", "4 4" },
            { "rR", "4 4" },
            { "RRR", "4 4 4 4 4 4" },
            { "estrella", "e s t 4 e Z a" },
            { "pie", "p j e" },
        };

        public static bool SelfTest(out string report)
        {
            string oy = _yeismo, oz = _zeta; bool oc = _caseMarks;
            _yeismo = "Z"; _zeta = "sdz"; _caseMarks = true;
            StringBuilder sb = new StringBuilder();
            int bad = 0, total = SelfTestVectors.GetLength(0);
            try
            {
                for (int k = 0; k < total; k++)
                {
                    string w = SelfTestVectors[k, 0], want = SelfTestVectors[k, 1], got = G2P(w);
                    if (got != want) { bad++; sb.AppendLine("FALLO  " + w + "  esperado [" + want + "]  obtenido [" + got + "]"); }
                }
                // una sola nota: la palabra larga sale entera, sin separar por sílabas
                string longw = LyricToJapanesePhonemes("cantando");
                if (longw != "k a n t a n d o") { bad++; sb.AppendLine("FALLO  cantando (palabra larga en una nota): [" + longw + "]"); }
                if (LyricToJapanesePhonemes("ka") == null) { bad++; sb.AppendLine("FALLO  'ka' deberia convertirse"); }
                if (LyricToJapanesePhonemes("[a]") != null) { bad++; sb.AppendLine("FALLO  '[a]' deberia ignorarse"); }
                // opciones de "Sonido de ll / y" (Z = zheismo, S = sheismo, j = yeismo)
                string[,] llv = new string[,]
                {
                    { "Z", "llave", "Z a b e" }, { "S", "llave", "S a b e" }, { "j", "llave", "j a b e" },
                    { "Z", "yo", "Z o" },        { "S", "yo", "S o" },        { "j", "yo", "j o" },
                    { "S", "estrella", "e s t 4 e S a" }, { "j", "estrella", "e s t 4 e j a" },
                    { "S", "lluvia", "S M b j a" },       { "j", "hoy", "o j" }, { "S", "y", "i" },
                    // Y en mayúscula ante vocal: j en todos los modos
                    { "Z", "Yo", "j o" }, { "S", "Yo", "j o" }, { "j", "Yo", "j o" },
                    { "Z", "maYa", "m a j a" }, { "S", "maYa", "m a j a" }, { "j", "maYa", "m a j a" },
                    { "Z", "hoY", "o j" }, { "S", "hoY", "o j" },
                    // sh: S en todos los modos
                    { "Z", "sha", "S a" }, { "S", "sha", "S a" }, { "j", "sha", "S a" },
                    // C mayúscula ante e / i: igual que la Z mayúscula; con el esquema predeterminado (sdz) da dz en todos los modos de ll / y
                    { "Z", "Ce", "dz e" }, { "S", "Ce", "dz e" }, { "j", "Ci", "dz i" }
                };
                for (int k = 0; k < llv.GetLength(0); k++)
                {
                    SetYeismo(llv[k, 0]);
                    string got = G2P(llv[k, 1]);
                    total++;
                    if (got != llv[k, 2]) { bad++; sb.AppendLine("FALLO  ll=" + llv[k, 0] + "  " + llv[k, 1] + "  esperado [" + llv[k, 2] + "]  obtenido [" + got + "]"); }
                }

                // esquemas de z y C (zeta=sdz|sts|sz): minúscula / MAYÚSCULA (la C mayúscula ante e / i sigue a la Z mayúscula)
                string[,] zv = new string[,]
                {
                    { "sdz", "zapato", "s a p a t o" }, { "sdz", "Zapato", "dz a p a t o" },
                    { "sdz", "ce", "s e" }, { "sdz", "Ce", "dz e" }, { "sdz", "Ci", "dz i" }, { "sdz", "Cielo", "dz j e 4 o" }, { "sdz", "Ca", "k a" },
                    { "sts", "zapato", "s a p a t o" }, { "sts", "Zapato", "ts a p a t o" },
                    { "sts", "ce", "s e" }, { "sts", "Ce", "ts e" }, { "sts", "Ci", "ts i" }, { "sts", "Cielo", "ts j e 4 o" }, { "sts", "haCer", "a ts e 4" }, { "sts", "Ca", "k a" }, { "sts", "Cha", "tS a" },
                    { "sz",  "zapato", "s a p a t o" }, { "sz",  "Zapato", "z a p a t o" },
                    { "sz",  "ce", "s e" }, { "sz",  "Ce", "z e" }, { "sz",  "Ci", "z i" }, { "sz",  "Cielo", "z j e 4 o" }, { "sz",  "Ca", "k a" }
                };
                for (int k = 0; k < zv.GetLength(0); k++)
                {
                    SetZeta(zv[k, 0]);
                    string got = G2P(zv[k, 1]);
                    total++;
                    if (got != zv[k, 2]) { bad++; sb.AppendLine("FALLO  zeta=" + zv[k, 0] + "  " + zv[k, 1] + "  esperado [" + zv[k, 2] + "]  obtenido [" + got + "]"); }
                }
            }
            catch (Exception e) { bad++; sb.AppendLine("EXCEPCION " + e); }
            finally { _yeismo = oy; _zeta = oz; _caseMarks = oc; }
            sb.AppendLine(bad == 0 ? "Autotest OK (" + total + " casos)." : "Autotest con " + bad + " fallo(s).");
            report = sb.ToString();
            return bad == 0;
        }
    }
}
