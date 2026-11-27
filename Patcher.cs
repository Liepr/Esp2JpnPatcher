// Núcleo del parcheador: inyecta en VOCALOID7.dll una llamada a Esp2JpnHook.dll
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Esp2Jpn
{
    public class PatchResult { public bool Ok; public string Message; }

    // Opciones que se aplican junto con el parche (también se recuerdan en Esp2Jpn.ini).
    public class PatchOptions
    {
        public bool ChangeNoteNames;                 // cambiar los nombres de las notas del piano roll (vocaloid7_notes.py)
        public string Notation = "american";         // american (C D E...) | solfege (Do Re Mi...)
        public string Accidentals = "sharps";        // sharps | flats
        public string Yeismo;                        // sonido de ll / y: Z | S | j  (null = no tocar el .ini)
        public string Zeta;                          // esquema de z y C (minúscula / MAYÚSCULA): sdz | sts | sz  (null = no tocar el .ini)

        public static string NormYeismo(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            if (v == "z") return "Z";
            if (v == "s") return "S";
            if (v == "j") return "j";
            return null;
        }

        public static string NormZeta(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            return v == "sdz" || v == "sts" || v == "sz" ? v : null;
        }

        public static PatchOptions FromIni(string dll)
        {
            PatchOptions o = new PatchOptions();
            string nn = Patcher.IniGet(dll, "notenames", "0").ToLowerInvariant();
            o.ChangeNoteNames = nn == "1" || nn == "true" || nn == "yes" || nn == "si";
            o.Notation = Patcher.IniGet(dll, "notation", "american").ToLowerInvariant() == "solfege" ? "solfege" : "american";
            o.Accidentals = Patcher.IniGet(dll, "accidentals", "sharps").ToLowerInvariant() == "flats" ? "flats" : "sharps";
            o.Yeismo = NormYeismo(Patcher.IniGet(dll, "ll", null));
            o.Zeta = NormZeta(Patcher.IniGet(dll, "zeta", null));
            return o;
        }

        // Para el modo consola:  ll=Z|S|j   zeta=sdz|sts|sz   notes=none|american|solfege   acc=sharps|flats
        public void Apply(string key, string value)
        {
            key = key.Trim().ToLowerInvariant(); value = value.Trim().ToLowerInvariant();
            if (key == "ll") { string y = NormYeismo(value); if (y != null) Yeismo = y; }
            else if (key == "zeta") { string z = NormZeta(value); if (z != null) Zeta = z; }
            else if (key == "notes")
            {
                if (value == "none" || value == "0" || value == "off") ChangeNoteNames = false;
                else if (value == "american" || value == "solfege") { ChangeNoteNames = true; Notation = value; }
            }
            else if (key == "acc" && (value == "sharps" || value == "flats")) Accidentals = value;
        }
    }

    public static class Patcher
    {
        public static string ZetaLabel(string z) { return z == "sz" ? "z \u2192 s, Z / C \u2192 z" : z == "sts" ? "z \u2192 s, Z / C \u2192 ts" : "z \u2192 s, Z / C \u2192 dz"; }
        public const string HookAsm = "Esp2JpnHook";
        const string LegacyHookDll = "HispaVoxHook.dll";   // nombre del hook en la v1.5 (renombrada y luego revertida) (se limpia al re-aplicar / restaurar)
        const string SvcType  = "Yamaha.VOCALOID.VSM.WIVSMPhonemeService";
        const string NoteType = "Yamaha.VOCALOID.VSM.WIVSMNote";

        public static string BackupPath(string dll) { return dll + ".esp2jpn.bak"; }

        // ---------- resolvedor que también busca el runtime de .NET instalado ----------
        static DefaultAssemblyResolver MakeResolver(string dllDir, Action<string> log)
        {
            var r = new DefaultAssemblyResolver();
            r.AddSearchDirectory(dllDir);
            var roots = new List<string>();
            string dr = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(dr)) roots.Add(dr);
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(pf)) roots.Add(Path.Combine(pf, "dotnet"));
            foreach (string root in roots)
            {
                string shared = Path.Combine(root, "shared");
                if (!Directory.Exists(shared)) continue;
                foreach (string fw in Directory.GetDirectories(shared))
                    foreach (string ver in Directory.GetDirectories(fw))
                        r.AddSearchDirectory(ver);
            }
            return r;
        }

        static bool Resolves(TypeReference t) { try { return t.Resolve() != null; } catch { return false; } }

        // Cecil necesita resolver los enum externos para reescribir valores por defecto.
        // Si el runtime no está accesible, se descartan solo esos valores (sin efecto en ejecución).
        static int StripUnresolvableConstants(ModuleDefinition m)
        {
            int n = 0;
            foreach (var ty in m.GetTypes())
            {
                foreach (var me in ty.Methods)
                    foreach (var p in me.Parameters)
                        if (p.HasConstant && p.ParameterType.MetadataType == MetadataType.ValueType && !Resolves(p.ParameterType))
                        { p.HasConstant = false; p.HasDefault = false; n++; }
                foreach (var f in ty.Fields)
                    if (f.HasConstant && f.FieldType.MetadataType == MetadataType.ValueType && !Resolves(f.FieldType))
                    { f.HasConstant = false; n++; }
                foreach (var pr in ty.Properties)
                    if (pr.HasConstant && pr.PropertyType.MetadataType == MetadataType.ValueType && !Resolves(pr.PropertyType))
                    { pr.HasConstant = false; n++; }
            }
            return n;
        }

        // ---------- VOCALOID7 abierto ----------
        // Procesos de VOCALOID7 en ejecución (nombre.exe). Un proceso cuenta solo si:
        //   (a) su nombre empieza por "VOCALOID7", o
        //   (b) tiene cargada ESTA VOCALOID7.dll (el editor con otro nombre de .exe).
        // Que un .exe esté en la misma carpeta que la DLL NO basta (p. ej. Esp2XSAMPA.exe).
        public static List<string> RunningVocaloid(string dll)
        {
            List<string> found = new List<string>();
            string full = null;
            try { full = Path.GetFullPath(dll); } catch { }
            int self = -1;
            try { self = System.Diagnostics.Process.GetCurrentProcess().Id; } catch { }
            System.Diagnostics.Process[] all;
            try { all = System.Diagnostics.Process.GetProcesses(); } catch { return found; }
            foreach (System.Diagnostics.Process p in all)
            {
                try
                {
                    if (p.Id == self) continue;
                    string n = p.ProcessName;
                    bool hit = n.StartsWith("VOCALOID7", StringComparison.OrdinalIgnoreCase) || LoadsDll(p, full);
                    if (hit && !found.Contains(n + ".exe")) found.Add(n + ".exe");
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return found;
        }

        // ¿El proceso tiene cargado exactamente este DLL? (sin acceso a sus módulos: false)
        static bool LoadsDll(System.Diagnostics.Process p, string fullDll)
        {
            if (string.IsNullOrEmpty(fullDll)) return false;
            try
            {
                foreach (System.Diagnostics.ProcessModule m in p.Modules)
                    if (string.Equals(m.FileName, fullDll, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        static string OpenMessage(List<string> running, string action)
        {
            return "VOCALOID7 está abierto (" + string.Join(", ", running.ToArray()) + ").\r\n" +
                   "Ciérralo por completo y vuelve a intentarlo: para " + action + " VOCALOID7 debe estar cerrado.";
        }

        // ¿El DLL lleva la sección ".nnotes" que añade vocaloid7_notes.py?
        static bool HasNotesSection(string dll)
        {
            try
            {
                using (FileStream fs = File.OpenRead(dll))
                {
                    byte[] b = new byte[Math.Min(4096, (int)Math.Min(fs.Length, 4096))];
                    int n = fs.Read(b, 0, b.Length);
                    byte[] key = System.Text.Encoding.ASCII.GetBytes(".nnotes");
                    for (int i = 0; i + key.Length <= n; i++)
                    {
                        int k = 0;
                        while (k < key.Length && b[i + k] == key[k]) k++;
                        if (k == key.Length) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ---------- Esp2Jpn.ini ----------
        public static string IniPath(string dll) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dll)), "Esp2Jpn.ini"); }

        public static bool IniExists(string dll)
        {
            try { return File.Exists(IniPath(dll)); } catch { return false; }
        }

        static bool IniKeyIs(string line, string key)
        {
            string s = line.Trim();
            if (s.StartsWith(";")) return false;
            int e = s.IndexOf('=');
            return e > 0 && s.Substring(0, e).Trim().ToLowerInvariant() == key;
        }

        public static string IniGet(string dll, string key, string def)
        {
            try
            {
                string p = IniPath(dll);
                if (!File.Exists(p)) return def;
                foreach (string l in File.ReadAllLines(p))
                    if (IniKeyIs(l, key)) { string s = l.Trim(); return s.Substring(s.IndexOf('=') + 1).Trim(); }
            }
            catch { }
            return def;
        }

        public static bool IniSet(string dll, string key, string value)
        {
            try
            {
                string p = IniPath(dll);
                List<string> lines = File.Exists(p) ? new List<string>(File.ReadAllLines(p)) : new List<string>();
                int idx = lines.FindIndex(delegate (string l) { return IniKeyIs(l, key); });
                string nl = key + "=" + value;
                if (idx >= 0) lines[idx] = nl; else lines.Add(nl);
                File.WriteAllLines(p, lines.ToArray());
                return true;
            }
            catch { return false; }
        }

        // ---------- nombres de nota (port de vocaloid7_notes.py) ----------
        const string SHARP = "\u266f", FLAT = "\u266d";
        // Nombres de las notas 0..11 (Do..Si) tal como vienen en VOCALOID7 (se validan antes de tocar nada)
        static readonly string[] OriginalNoteNames = { "C", "C" + SHARP, "D", "E" + FLAT, "E", "F", "F" + SHARP, "G", "G" + SHARP, "A", "B" + FLAT, "B" };

        // notation: "american" | "solfege"      accidentals: "sharps" | "flats"
        public static string[] NoteNames(string notation, string accidentals)
        {
            string[] nat = notation == "solfege" ? new[] { "Do", "Re", "Mi", "Fa", "Sol", "La", "Si" }
                                                 : new[] { "C", "D", "E", "F", "G", "A", "B" };
            bool flats = accidentals == "flats";
            int[] natIdx = { 0, -1, 1, -1, 2, 3, -1, 4, -1, 5, -1, 6 };      // nota 0..11 -> natural (o -1 si es alterada)
            int[] sharpOf = { -1, 0, -1, 1, -1, -1, 3, -1, 4, -1, 5, -1 };   // alterada -> natural inferior (sostenido)
            int[] flatOf = { -1, 1, -1, 2, -1, -1, 4, -1, 5, -1, 6, -1 };    // alterada -> natural superior (bemol)
            string[] r = new string[12];
            for (int i = 0; i < 12; i++)
                r[i] = natIdx[i] >= 0 ? nat[natIdx[i]] : nat[flats ? flatOf[i] : sharpOf[i]] + (flats ? FLAT : SHARP);
            return r;
        }

        // Cambia en el IL (a) los 12 ldstr de NoteNumberToName y (b) el ldstr "C" de las etiquetas de octava del teclado.
        // Devuelve null si todo fue bien o un mensaje de error (en ese caso no se ha modificado nada).
        static string PatchNoteNames(ModuleDefinition mod, string[] names, Action<string> log)
        {
            List<Instruction[]> tables = new List<Instruction[]>();
            List<Instruction> kbs = new List<Instruction>();
            foreach (TypeDefinition t in mod.GetTypes())
                foreach (MethodDefinition m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    try
                    {
                        var ins = m.Body.Instructions;
                        for (int k = 0; k < ins.Count; k++)
                        {
                            Instruction i0 = ins[k];
                            // tabla: switch de 12 destinos, cada uno un ldstr con el nombre original (en orden)
                            if (i0.OpCode.Code == Code.Switch)
                            {
                                Instruction[] tg = i0.Operand as Instruction[];
                                if (tg != null && tg.Length == 12)
                                {
                                    Instruction[] ld = new Instruction[12]; bool ok = true;
                                    for (int q = 0; q < 12 && ok; q++)
                                    {
                                        ok = tg[q] != null && tg[q].OpCode.Code == Code.Ldstr && (tg[q].Operand as string) == OriginalNoteNames[q];
                                        if (ok) ld[q] = tg[q];
                                    }
                                    if (ok) tables.Add(ld);
                                }
                            }
                            // teclado:  ldc.i4.s 12 ; rem ; brtrue(.s) ; ldloc(.s) 5 ; ldstr "C"
                            if ((i0.OpCode.Code == Code.Ldc_I4_S || i0.OpCode.Code == Code.Ldc_I4) && k + 4 < ins.Count &&
                                System.Convert.ToInt32(i0.Operand) == 12 &&
                                ins[k + 1].OpCode.Code == Code.Rem &&
                                (ins[k + 2].OpCode.Code == Code.Brtrue_S || ins[k + 2].OpCode.Code == Code.Brtrue) &&
                                (ins[k + 3].OpCode.Code == Code.Ldloc_S || ins[k + 3].OpCode.Code == Code.Ldloc) &&
                                ins[k + 3].Operand is VariableDefinition && ((VariableDefinition)ins[k + 3].Operand).Index == 5 &&
                                ins[k + 4].OpCode.Code == Code.Ldstr && (ins[k + 4].Operand as string) == "C")
                                kbs.Add(ins[k + 4]);
                        }
                    }
                    catch (Exception) { }                  // cuerpo que Cecil no pudo leer: se ignora
                }
            if (tables.Count != 1) return "no se encontró la tabla de nombres de nota (versión de VOCALOID7 distinta)";
            if (kbs.Count != 1) return "no se encontró la etiqueta «C» del teclado (versión de VOCALOID7 distinta)";
            for (int i = 0; i < 12; i++) tables[0][i].Operand = names[i];
            kbs[0].Operand = names[0];
            return null;
        }

        // ---------- estado ----------
        public static string GetStatus(string dll)
        {
            if (!File.Exists(dll)) return "Archivo no encontrado.";
            try
            {
                using (var m = ModuleDefinition.ReadModule(dll))
                {
                    if (m.GetType(SvcType) == null) return "No parece ser el VOCALOID7.dll del editor (falta " + SvcType + ").";
                    TypeDefinition br = m.GetType("Esp2Jpn.Bridge");
                    if (br == null) return "Sin parchear (compatible)";
                    return br.Methods.Any(x => x.Name == "End") && br.Methods.Any(x => x.Name == "Begin" && x.Parameters.Count == 3) ? "PARCHEADO"
                        : "PARCHEADO con versión anterior (pulsa Aplicar / re-aplicar para activar «no repartir» y Esp2XSAMPA)";
                }
            }
            catch (Exception e) { return "No se pudo leer como ensamblado .NET: " + e.Message; }
        }

        // ---------- parchear ----------
        public static PatchResult Patch(string dll, string hookDllSource, Action<string> log)
        {
            return Patch(dll, hookDllSource, log, PatchOptions.FromIni(dll));
        }

        public static PatchResult Patch(string dll, string hookDllSource, Action<string> log, PatchOptions opt)
        {
            try
            {
                if (opt == null) opt = new PatchOptions();
                if (!File.Exists(dll)) return Fail("No existe: " + dll);
                if (!File.Exists(hookDllSource)) return Fail("Falta " + HookAsm + ".dll junto al parcheador.");
                // VOCALOID7 debe estar CERRADO: mantiene VOCALOID7.dll en uso y, además, el parche solo se carga al arrancar.
                List<string> running = RunningVocaloid(dll);
                if (running.Count > 0) return Fail(OpenMessage(running, "aplicar el parche"));
                string dir = Path.GetDirectoryName(Path.GetFullPath(dll));
                // DLL ya modificado con vocaloid7_notes.py (sin copia propia): la "original" quedaría con los nombres cambiados
                if (!File.Exists(BackupPath(dll)) && HasNotesSection(dll))
                    return Fail("Este VOCALOID7.dll ya fue modificado con vocaloid7_notes.py.\r\n" +
                                "Restaura primero el original (copia VOCALOID7.dll.bak -> VOCALOID7.dll) y vuelve a aplicar: " +
                                "este parcheador ya incluye los nombres de nota.");

                var rp = new ReaderParameters { AssemblyResolver = MakeResolver(dir, log), InMemory = true };
                ModuleDefinition mod = ModuleDefinition.ReadModule(dll, rp);
                if (mod.GetType("Esp2Jpn.Bridge") != null)
                {
                    mod.Dispose();
                    string b0 = BackupPath(dll);
                    if (!File.Exists(b0)) return Fail("Ya está parcheado y no hay copia de seguridad para re-aplicar.");
                    File.Copy(b0, dll, true);
                    log("Parche anterior retirado (se re-aplica desde la copia original).");
                    mod = ModuleDefinition.ReadModule(dll, rp);
                }

                TypeDefinition svc = mod.GetType(SvcType), note = mod.GetType(NoteType);
                if (svc == null || note == null) { mod.Dispose(); return Fail("Estructura de VOCALOID7.dll no reconocida (versión distinta)."); }

                MethodDefinition mAuto = svc.Methods.FirstOrDefault(x => x.Name == "SetLyricsAndPhonemesByAutoPriority" && x.Parameters.Count == 3);
                MethodDefinition mLang = svc.Methods.FirstOrDefault(x => x.Name == "SetLyricsAndPhonemesByLangID" && x.Parameters.Count == 4);
                MethodDefinition mSetPh = svc.Methods.FirstOrDefault(x => x.Name == "SetPhonemesByLangID" && x.Parameters.Count == 3);
                MethodDefinition gLang = note.Methods.FirstOrDefault(x => x.Name == "get_LangID");
                MethodDefinition sLyr = note.Methods.FirstOrDefault(x => x.Name == "set_Lyric");
                MethodDefinition nSetPh = note.Methods.FirstOrDefault(x => x.Name == "SetPhonemes" && x.Parameters.Count == 3);
                // candado "Protect Phonetic Symbol" del panel NOTE: sin él el motor puede regenerar los fonemas desde la letra
                MethodDefinition gLyr = note.Methods.FirstOrDefault(x => x.Name == "get_Lyric" && x.Parameters.Count == 0);
                MethodDefinition gProt = note.Methods.FirstOrDefault(x => x.Name == "get_IsProtected" && x.Parameters.Count == 0 && x.ReturnType.FullName == "System.Boolean");
                MethodDefinition sProt = note.Methods.FirstOrDefault(x => x.Name == "set_IsProtected" && x.Parameters.Count == 1 && x.Parameters[0].ParameterType.FullName == "System.Boolean");
                if (sProt == null) log("Aviso: no se encontró WIVSMNote.set_IsProtected; no se activará el candado de fonemas.");
                if (mAuto == null || mLang == null || mSetPh == null || gLang == null || sLyr == null || nSetPh == null)
                { mod.Dispose(); return Fail("No se encontraron todos los métodos necesarios (versión distinta de VOCALOID7)."); }

                // Nombres de nota (vocaloid7_notes.py). Se hace ANTES de inyectar nada para escanear solo el código original.
                string notesMsg = null; bool notesOk = false;
                if (opt.ChangeNoteNames)
                {
                    notesMsg = PatchNoteNames(mod, NoteNames(opt.Notation, opt.Accidentals), log);
                    notesOk = notesMsg == null;
                    if (notesOk) log("Nombres de nota cambiados: " + string.Join(" ", NoteNames(opt.Notation, opt.Accidentals)));
                    else log("Aviso (nombres de nota): " + notesMsg);
                }

                MethodDefinition convertNotes, convertPre, bridgeBegin, bridgeEnd;
                MethodDefinition convert = BuildBridge(mod, out convertNotes, out convertPre, out bridgeBegin, out bridgeEnd);

                // PRIMERO la guarda "no repartir" (anula nextNote antes de que el motor nativo reparta sílabas
                // entre notas contiguas); después el postfix, que fija los fonemas completos en la nota actual.
                InjectNoSplit(mAuto, mod, convertPre, gLang, 1, 2, log);
                InjectNoSplit(mLang, mod, convertPre, gLang, 1, 2, log);
                InjectPostfix(mAuto, mod, convert, bridgeEnd, gLang, sLyr, mSetPh, nSetPh, sProt, gLyr, gProt, 1, 2, log);
                InjectPostfix(mLang, mod, convert, bridgeEnd, gLang, sLyr, mSetPh, nSetPh, sProt, gLyr, gProt, 1, 2, log);
                // v4: al ENTRAR guardan el estado de la nota siguiente (se aplica al final para quedar antes de todo lo demás)
                InjectBegin(mAuto, mod, bridgeBegin, mAuto.Parameters[0], log);
                InjectBegin(mLang, mod, bridgeBegin, mLang.Parameters[0], log);

                // Capa exterior (extensión estática): corre DESPUÉS de normalizar fonemas, que puede pisar los nuestros
                TypeDefinition ext = mod.GetType("Yamaha.VOCALOID.WIVSMNoteExtension");
                int nExt = 0;
                if (ext != null)
                    foreach (MethodDefinition em in ext.Methods.ToList())
                    {
                        if (em.Name != "SetLyricsAndPhonemesAndNormalizePhonemes" || !em.IsStatic || !em.HasBody) continue;
                        if (em.Parameters.Count < 2 || em.Parameters[0].ParameterType.FullName != NoteType || em.Parameters[1].ParameterType.FullName != "System.String") continue;
                        InjectPostfixExt(em, mod, convert, bridgeEnd, gLang, nSetPh, sProt, gLyr, gProt, log);
                        InjectBegin(em, mod, bridgeBegin, em.Parameters[0], log);
                        nExt++;
                    }
                // Cambio de banco de voz / normalización: reconvierte las notas ya existentes (canciones en español)
                int nTr = 0;
                foreach (MethodDefinition tm in mod.GetTypes().Where(t => t.FullName == "Yamaha.VOCALOID.VSM.WIVSMMidiTrack").SelectMany(t => t.Methods).ToList())
                {
                    bool hit = (tm.Name == "NormalizeAllPhonemes" && tm.Parameters.Count == 0) ||
                               ((tm.Name == "SetVoiceBank" || tm.Name == "SetVoiceBankID") && tm.Parameters.Count == 1);
                    if (!hit || !tm.HasBody || tm.IsStatic) continue;
                    InjectNotesPostfix(tm, mod, convertNotes, log); nTr++;
                }
                foreach (MethodDefinition pm in mod.GetTypes().Where(t => t.FullName == "Yamaha.VOCALOID.WIVSMMidiPartExtension").SelectMany(t => t.Methods).ToList())
                {
                    if (pm.Name != "NormalizeAllPhonemes" || !pm.IsStatic || !pm.HasBody || pm.Parameters.Count != 1) continue;
                    InjectNotesPostfix(pm, mod, convertNotes, log); nTr++;
                }
                if (nTr == 0) log("Aviso: no se encontraron los métodos de pista (cambio de banco de voz no cubierto).");
                if (nExt == 0) log("Aviso: no se encontró SetLyricsAndPhonemesAndNormalizePhonemes (solo se parchean las capas internas).");

                // firma de nombre seguro ya no válida
                mod.Attributes &= ~ModuleAttributes.StrongNameSigned;

                int stripped = StripUnresolvableConstants(mod);
                if (stripped > 0) log("Aviso: " + stripped + " valores por defecto de enum externos descartados (runtime .NET no encontrado; inocuo).");

                // copia de seguridad (solo la primera vez)
                string bak = BackupPath(dll);
                if (!File.Exists(bak)) { File.Copy(dll, bak); log("Copia de seguridad: " + bak); }

                string tmp = dll + ".esp2jpn.tmp";
                mod.Write(tmp);
                mod.Dispose();
                File.Copy(tmp, dll, true);
                File.Delete(tmp);

                File.Copy(hookDllSource, Path.Combine(dir, HookAsm + ".dll"), true);
                try { string old = Path.Combine(dir, LegacyHookDll); if (File.Exists(old)) { File.Delete(old); log("Hook de la versión anterior eliminado: " + LegacyHookDll); } } catch { }
                string ini = Path.Combine(dir, "Esp2Jpn.ini");
                if (!File.Exists(ini)) File.WriteAllText(ini, "; enabled=0 desactiva la conversion sin restaurar el DLL\r\nenabled=1\r\n; langs: idiomas de nota a convertir (0=japones, * = todos)\r\nlangs=0\r\n; nosplit=1: todos los fonemas en la nota donde se escribe la letra (VOCALOID no reparte silabas en notas contiguas)\r\nnosplit=1\r\n; case=1: las mayusculas U I J G R C Y Z A E O tienen el significado de Esp2XSAMPA (0 = ignorarlas)\r\ncase=1\r\n; words=1: texto con varias palabras (Insert Lyrics) -> una palabra por nota, separadas por espacio (0 = comportamiento del motor)\r\nwords=1\r\n; ll: sonido de ll / y  (Z, S o j)\r\nll=Z\r\n; zeta: sonido de z y c (minuscula / MAYUSCULA; la C solo ante e, i)  (sdz = s / dz, sts = s / ts, sz = s / z)\r\nzeta=sdz\r\n; nombres de nota (solo los usa el parcheador): notenames=1|0  notation=american|solfege  accidentals=sharps|flats\r\nnotenames=0\r\nnotation=american\r\naccidentals=sharps\r\n");
                // recordar las opciones elegidas (el hook solo lee ll; el resto lo lee el parcheador al abrirse)
                if (opt.Yeismo != null) IniSet(dll, "ll", opt.Yeismo);
                if (opt.Zeta != null) IniSet(dll, "zeta", opt.Zeta);
                IniSet(dll, "notenames", opt.ChangeNoteNames ? "1" : "0");
                IniSet(dll, "notation", opt.Notation);
                IniSet(dll, "accidentals", opt.Accidentals);

                string msg = "Parche aplicado. Ya puedes abrir VOCALOID7.";
                if (opt.Yeismo != null) msg += "\r\nSonido de ll / y: " + opt.Yeismo + ".";
                if (opt.Zeta != null) msg += "\r\nSonido de z / c: " + ZetaLabel(opt.Zeta) + ".";
                if (opt.ChangeNoteNames)
                    msg += notesOk ? "\r\nNombres de nota: " + string.Join(" ", NoteNames(opt.Notation, opt.Accidentals)) + "."
                                   : "\r\nAVISO: los nombres de nota NO se cambiaron (" + notesMsg + ").";
                return new PatchResult { Ok = true, Message = msg };
            }
            catch (UnauthorizedAccessException) { return Fail("Sin permisos de escritura. Ejecuta el parcheador como administrador y cierra VOCALOID7."); }
            catch (IOException e) { return Fail("Error de E/S (¿VOCALOID7 está abierto?): " + e.Message); }
            catch (Exception e) { return Fail("Error: " + e); }
        }

        static PatchResult Fail(string m) { return new PatchResult { Ok = false, Message = m }; }

        // Puente inyectado en el propio DLL:  Esp2Jpn.Bridge.Convert(string lyric, int langID)
        // Carga Esp2JpnHook.dll por ruta (junto a VOCALOID7.dll), llama a Esp2Jpn.Hook.Convert por reflexión
        // y devuelve null ante cualquier fallo (anotándolo en %TEMP%\Esp2Jpn-bridge.log).
        public static MethodDefinition BuildBridge(ModuleDefinition mod, out MethodDefinition convertNotes, out MethodDefinition convertPre,
            out MethodDefinition begin, out MethodDefinition end)
        {
            TypeReference tObj = mod.TypeSystem.Object, tStr = mod.TypeSystem.String, tInt = mod.TypeSystem.Int32,
                          tVoid = mod.TypeSystem.Void, tBool = mod.TypeSystem.Boolean;
            var core = mod.TypeSystem.CoreLibrary;
            Func<string, string, TypeReference> T = (ns, n) => new TypeReference(ns, n, mod, core);
            TypeReference tAsm = T("System.Reflection", "Assembly"), tType = T("System", "Type"),
                          tMI = T("System.Reflection", "MethodInfo"), tMB = T("System.Reflection", "MethodBase"),
                          tPath = T("System.IO", "Path"), tFile = T("System.IO", "File"), tExc = T("System", "Exception");

            Func<TypeReference, string, TypeReference, bool, TypeReference[], MethodReference> M = (decl, name, ret, hasThis, ps) =>
            {
                var r = new MethodReference(name, ret, decl) { HasThis = hasThis };
                foreach (var x in ps) r.Parameters.Add(new ParameterDefinition(x));
                return r;
            };
            var getExec = M(tAsm, "GetExecutingAssembly", tAsm, false, new TypeReference[0]);
            var getLoc  = M(tAsm, "get_Location", tStr, true, new TypeReference[0]);
            var getDir  = M(tPath, "GetDirectoryName", tStr, false, new[] { tStr });
            var combine = M(tPath, "Combine", tStr, false, new[] { tStr, tStr });
            var loadFrom= M(tAsm, "LoadFrom", tAsm, false, new[] { tStr });
            var getType = M(tAsm, "GetType", tType, true, new[] { tStr });
            var getMeth = M(tType, "GetMethod", tMI, true, new[] { tStr });
            var invoke  = M(tMB, "Invoke", tObj, true, new TypeReference[] { tObj, new ArrayType(tObj) });
            var toStr   = M(tObj, "ToString", tStr, true, new TypeReference[0]);

            var bt = new TypeDefinition("Esp2Jpn", "Bridge",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, tObj);
            Func<string, string, TypeReference[], MethodDefinition> Make = (mname, hookName, ps) =>
            {
            var fMi = new FieldDefinition("mi_" + mname, FieldAttributes.Private | FieldAttributes.Static, tMI);
            var fTried = new FieldDefinition("tried_" + mname, FieldAttributes.Private | FieldAttributes.Static, tBool);
            bt.Fields.Add(fMi); bt.Fields.Add(fTried);

            var m = new MethodDefinition(mname, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, tStr);
            for (int pi = 0; pi < ps.Length; pi++)
                m.Parameters.Add(new ParameterDefinition("a" + pi, ParameterAttributes.None, ps[pi]));
            bt.Methods.Add(m);
            var body = m.Body; body.InitLocals = true;
            var vRet = new VariableDefinition(tStr); var vEx = new VariableDefinition(tExc);
            body.Variables.Add(vRet); body.Variables.Add(vEx);
            var il = body.GetILProcessor();

            var LOAD  = il.Create(OpCodes.Nop);
            var HAVE  = il.Create(OpCodes.Ldsfld, fMi);
            var H0    = il.Create(OpCodes.Stloc, vEx);
            var END   = il.Create(OpCodes.Ldloc, vRet);

            var T0 = il.Create(OpCodes.Ldsfld, fMi);
            il.Append(T0); il.Append(il.Create(OpCodes.Brtrue, HAVE));
            il.Append(il.Create(OpCodes.Ldsfld, fTried)); il.Append(il.Create(OpCodes.Brfalse, LOAD));
            il.Append(il.Create(OpCodes.Leave, END));   // FIX: salir del try hacia FUERA del handler (vRet ya es null)
            il.Append(LOAD);
            il.Append(il.Create(OpCodes.Ldc_I4_1)); il.Append(il.Create(OpCodes.Stsfld, fTried));
            il.Append(il.Create(OpCodes.Call, getExec)); il.Append(il.Create(OpCodes.Callvirt, getLoc));
            il.Append(il.Create(OpCodes.Call, getDir));
            il.Append(il.Create(OpCodes.Ldstr, HookAsm + ".dll")); il.Append(il.Create(OpCodes.Call, combine));
            il.Append(il.Create(OpCodes.Call, loadFrom));
            il.Append(il.Create(OpCodes.Ldstr, "Esp2Jpn.Hook")); il.Append(il.Create(OpCodes.Callvirt, getType));
            il.Append(il.Create(OpCodes.Ldstr, hookName)); il.Append(il.Create(OpCodes.Callvirt, getMeth));
            il.Append(il.Create(OpCodes.Stsfld, fMi));
            il.Append(HAVE);
            il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Ldc_I4, ps.Length)); il.Append(il.Create(OpCodes.Newarr, tObj));
            for (int pi = 0; pi < ps.Length; pi++)
            {
                il.Append(il.Create(OpCodes.Dup)); il.Append(il.Create(OpCodes.Ldc_I4, pi));
                il.Append(il.Create(OpCodes.Ldarg, m.Parameters[pi]));
                if (ps[pi] == tInt) il.Append(il.Create(OpCodes.Box, tInt));
                il.Append(il.Create(OpCodes.Stelem_Ref));
            }
            il.Append(il.Create(OpCodes.Callvirt, invoke));
            il.Append(il.Create(OpCodes.Isinst, tStr)); il.Append(il.Create(OpCodes.Stloc, vRet));
            il.Append(il.Create(OpCodes.Leave, END));
            // ante cualquier fallo: se ignora en silencio (sin archivos de registro) y se devuelve null
            il.Append(H0);
            il.Append(il.Create(OpCodes.Leave, END));
            il.Append(END); il.Append(il.Create(OpCodes.Ret));

            body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
                { TryStart = T0, TryEnd = H0, HandlerStart = H0, HandlerEnd = END, CatchType = tExc });

            return m;
            };
            MethodDefinition mConvert = Make("Convert", "Convert", new[] { tStr, tInt });
            convertNotes = Make("ConvertNotes", "ConvertNotes", new[] { tObj });
            convertPre = Make("ConvertPre", "ConvertPre", new[] { tStr, tInt });
            begin = Make("Begin", "Begin", new[] { tObj, tObj, tStr });
            end = Make("End", "End", new[] { tObj, tStr });
            mod.Types.Add(bt);
            return mConvert;
        }

        // Inyección PREVIA (guarda "no repartir"). nextNote es un parámetro ref (WIVSMNote&): el motor nativo usa
        // esa nota para repartir sílabas/fonemas de una palabra larga entre notas contiguas. Si el conversor se
        // hará cargo de la letra, se escribe null EN el valor referenciado (nunca en la referencia) y se
        // restaura el valor original al salir, para no alterar al llamador:
        //   if (note != null && lyrics != null && Bridge.ConvertPre(lyrics, langID) != null)
        //       { saved = nextNote; nextNote = null; nulled = true; }
        //   ... método original ...   if (nulled) nextNote = saved;
        static void InjectNoSplit(MethodDefinition m, ModuleDefinition mod, MethodReference convertPre,
            MethodDefinition getLang, int argNote, int argLyr, Action<string> log)
        {
            ParameterDefinition pNext = m.Parameters.FirstOrDefault(p => p.Name == "nextNote");
            ParameterDefinition pLang = m.Parameters.FirstOrDefault(p => p.Name == "langID" && p.ParameterType.FullName == "System.Int32");
            if (pNext == null) { log("Aviso: " + m.Name + " no tiene parámetro nextNote; no se aplica «no repartir» ahí."); return; }
            ByReferenceType brt = pNext.ParameterType as ByReferenceType;
            if (brt == null) { log("Aviso: nextNote de " + m.Name + " no es ref; no se aplica «no repartir» ahí."); return; }
            var il = m.Body.GetILProcessor();
            m.Body.InitLocals = true;
            var vSaved = new VariableDefinition(brt.ElementType);
            var vNulled = new VariableDefinition(mod.TypeSystem.Boolean);
            m.Body.Variables.Add(vSaved); m.Body.Variables.Add(vNulled);
            Instruction first = m.Body.Instructions[0];
            ParameterDefinition pNote = m.Parameters[argNote - 1], pLyr = m.Parameters[argLyr - 1];

            // salidas: cada ret -> bloque que restaura nextNote
            bool hasRet = m.ReturnType.MetadataType != MetadataType.Void;
            VariableDefinition vRes = null;
            if (hasRet) { vRes = new VariableDefinition(m.ReturnType); m.Body.Variables.Add(vRes); }
            Instruction REST = hasRet ? il.Create(OpCodes.Stloc, vRes) : il.Create(OpCodes.Nop);
            Instruction DONE = hasRet ? il.Create(OpCodes.Ldloc, vRes) : il.Create(OpCodes.Ret);
            foreach (var ins in m.Body.Instructions.ToList())
                if (ins.OpCode == OpCodes.Ret) { ins.OpCode = OpCodes.Br; ins.Operand = REST; }
            il.Append(REST);
            il.Append(il.Create(OpCodes.Ldloc, vNulled)); il.Append(il.Create(OpCodes.Brfalse, DONE));
            il.Append(il.Create(OpCodes.Ldarg, pNext)); il.Append(il.Create(OpCodes.Ldloc, vSaved)); il.Append(il.Create(OpCodes.Stind_Ref));
            il.Append(DONE);
            if (hasRet) il.Append(il.Create(OpCodes.Ret));

            // entrada
            Action<Instruction> add = x => il.InsertBefore(first, x);
            add(il.Create(OpCodes.Ldarg, pNote)); add(il.Create(OpCodes.Brfalse, first));
            add(il.Create(OpCodes.Ldarg, pLyr));  add(il.Create(OpCodes.Brfalse, first));
            add(il.Create(OpCodes.Ldarg, pLyr));
            if (pLang != null) add(il.Create(OpCodes.Ldarg, pLang));
            else { add(il.Create(OpCodes.Ldarg, pNote)); add(il.Create(OpCodes.Callvirt, mod.ImportReference(getLang))); }
            add(il.Create(OpCodes.Call, convertPre));
            add(il.Create(OpCodes.Brfalse, first));
            add(il.Create(OpCodes.Ldarg, pNext)); add(il.Create(OpCodes.Ldind_Ref)); add(il.Create(OpCodes.Stloc, vSaved));
            add(il.Create(OpCodes.Ldarg, pNext)); add(il.Create(OpCodes.Ldnull)); add(il.Create(OpCodes.Stind_Ref));
            add(il.Create(OpCodes.Ldc_I4_1)); add(il.Create(OpCodes.Stloc, vNulled));
            log("Inyectado (no repartir) en " + m.Name);
        }

        // Inyección POSTERIOR: primero corre el motor nativo (que fija el idioma real de la nota) y,
        // justo antes de devolver, se hace:
        //   if (note != null && lyrics != null) {
        //       string ph = Hook.Convert(lyrics, note.LangID);
        //       if (ph != null) {
        //           if (!res) note.Lyric = lyrics;
        //           if (SetPhonemesByLangID(note, ph, langID) || note.SetPhonemes(ph, true, langID)) res = true;
        //       }
        //   }
        //   return res;
        static void InjectPostfix(MethodDefinition m, ModuleDefinition mod, MethodReference convert, MethodReference bridgeEnd,
            MethodDefinition getLang, MethodDefinition setLyric, MethodDefinition setPh, MethodDefinition noteSetPh,
            MethodDefinition setProt, MethodDefinition getLyric, MethodDefinition getProt, int argNote, int argLyr, Action<string> log)
        {
            var il = m.Body.GetILProcessor();
            var res = new VariableDefinition(mod.TypeSystem.Boolean);
            var ph = new VariableDefinition(mod.TypeSystem.String);
            m.Body.Variables.Add(res); m.Body.Variables.Add(ph);
            m.Body.InitLocals = true;

            Func<int, Instruction> ldarg = i => il.Create(OpCodes.Ldarg, m.Parameters[i - 1]);
            Action<Instruction> addEnd = ins => il.Append(ins);

            // Guarda: al entrar, recordar letra anterior y si la nota ya estaba protegida.
            // Si la letra no cambió y ya estaba protegida (edición manual de fonemas), no se reconvierte.
            bool guard = getLyric != null && getProt != null;
            VariableDefinition oldLyr = null, wasProt = null;
            if (guard)
            {
                oldLyr = new VariableDefinition(mod.TypeSystem.String); wasProt = new VariableDefinition(mod.TypeSystem.Boolean);
                m.Body.Variables.Add(oldLyr); m.Body.Variables.Add(wasProt);
                Instruction first = m.Body.Instructions[0];
                il.InsertBefore(first, ldarg(argNote)); il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
                il.InsertBefore(first, ldarg(argNote)); il.InsertBefore(first, il.Create(OpCodes.Callvirt, mod.ImportReference(getLyric))); il.InsertBefore(first, il.Create(OpCodes.Stloc, oldLyr));
                il.InsertBefore(first, ldarg(argNote)); il.InsertBefore(first, il.Create(OpCodes.Callvirt, mod.ImportReference(getProt))); il.InsertBefore(first, il.Create(OpCodes.Stloc, wasProt));
            }

            var POST = il.Create(OpCodes.Stloc, res);
            var END  = ldarg(argNote);                       // v4: inicio de la reparación de letra (todas las salidas pasan por aquí)
            var RET  = il.Create(OpCodes.Ldloc, res);
            var OK   = setProt != null ? il.Create(OpCodes.Ldarg, m.Parameters[argNote - 1]) : il.Create(OpCodes.Ldc_I4_1);
            // al cambiar la letra de una nota ya protegida, se quita el candado antes de escribir los fonemas
            // (una nota protegida ignora SetPhonemes); más abajo se vuelve a activar.
            var SKIPLYR = setProt != null ? il.Create(OpCodes.Ldarg, m.Parameters[argNote - 1]) : il.Create(OpCodes.Ldarg_0);

            // cada 'ret' original -> salta al bloque posterior (con el bool en la pila)
            foreach (var ins in m.Body.Instructions.ToList())
                if (ins.OpCode == OpCodes.Ret) { ins.OpCode = OpCodes.Br; ins.Operand = POST; }

            il.Append(POST);
            il.Append(ldarg(argNote)); il.Append(il.Create(OpCodes.Brfalse, END));
            il.Append(ldarg(argLyr));  il.Append(il.Create(OpCodes.Brfalse, END));
            Instruction CONT = ldarg(argLyr);
            if (guard)
            {
                il.Append(ldarg(argLyr)); il.Append(il.Create(OpCodes.Ldloc, oldLyr)); il.Append(il.Create(OpCodes.Call, StrEquals(mod)));
                il.Append(il.Create(OpCodes.Brfalse, CONT));
                il.Append(il.Create(OpCodes.Ldloc, wasProt)); il.Append(il.Create(OpCodes.Brtrue, END));
            }
            il.Append(CONT);  il.Append(ldarg(argNote)); il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(getLang)));
            il.Append(il.Create(OpCodes.Call, convert));
            il.Append(il.Create(OpCodes.Stloc, ph));
            il.Append(il.Create(OpCodes.Ldloc, ph)); il.Append(il.Create(OpCodes.Brfalse, END));
            // si el motor falló, fijar al menos la letra
            il.Append(il.Create(OpCodes.Ldloc, res)); il.Append(il.Create(OpCodes.Brtrue, SKIPLYR));
            il.Append(ldarg(argNote)); il.Append(ldarg(argLyr)); il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(setLyric)));
            il.Append(SKIPLYR);
            if (setProt != null)
            {
                il.Append(il.Create(OpCodes.Ldc_I4_0)); il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(setProt)));
                il.Append(il.Create(OpCodes.Ldarg_0));
            }
            il.Append(ldarg(argNote)); il.Append(il.Create(OpCodes.Ldloc, ph));
            il.Append(ldarg(argNote)); il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(getLang)));
            il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(setPh)));
            il.Append(il.Create(OpCodes.Brtrue, OK));
            // plan B: fijar fonemas directamente en la nota (sin validar contra el banco)
            il.Append(ldarg(argNote)); il.Append(il.Create(OpCodes.Ldloc, ph)); il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(ldarg(argNote)); il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(getLang)));
            il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(noteSetPh)));
            il.Append(il.Create(OpCodes.Brfalse, END));
            il.Append(OK);
            if (setProt != null)
            {
                // activar el candado de fonemas de la nota (note.IsProtected = true)
                il.Append(il.Create(OpCodes.Ldc_I4_1)); il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(setProt)));
                il.Append(il.Create(OpCodes.Ldc_I4_1));
            }
            il.Append(il.Create(OpCodes.Stloc, res));
            il.Append(END); il.Append(ldarg(argLyr)); il.Append(il.Create(OpCodes.Call, bridgeEnd)); il.Append(il.Create(OpCodes.Pop));
            il.Append(RET); il.Append(il.Create(OpCodes.Ret));
            log("Inyectado en " + m.Name);
        }

        // Postfix para el método estático de extensión (note = param 0, lyrics = param 1).
        // Tras normalizar, vuelve a fijar los fonemas convertidos:
        //   if (note != null && lyrics != null) { ph = Convert(lyrics, note.LangID); if (ph != null) note.SetPhonemes(ph, true, note.LangID); }
        static void InjectPostfixExt(MethodDefinition m, ModuleDefinition mod, MethodReference convert, MethodReference bridgeEnd,
            MethodDefinition getLang, MethodDefinition noteSetPh, MethodDefinition setProt, MethodDefinition getLyric, MethodDefinition getProt, Action<string> log)
        {
            var il = m.Body.GetILProcessor();
            bool hasRet = m.ReturnType.MetadataType != MetadataType.Void;
            var ph = new VariableDefinition(mod.TypeSystem.String);
            m.Body.Variables.Add(ph);
            VariableDefinition res = null;
            if (hasRet) { res = new VariableDefinition(m.ReturnType); m.Body.Variables.Add(res); }
            m.Body.InitLocals = true;

            Instruction POST = hasRet ? il.Create(OpCodes.Stloc, res) : il.Create(OpCodes.Nop);
            ParameterDefinition pNote = m.Parameters[0], pLyr = m.Parameters[1];
            Instruction END  = il.Create(OpCodes.Ldarg, pNote);   // v4: inicio de la reparación de letra
            Instruction RET  = hasRet ? il.Create(OpCodes.Ldloc, res) : il.Create(OpCodes.Ret);

            bool guard = getLyric != null && getProt != null;
            VariableDefinition oldLyr = null, wasProt = null;
            if (guard)
            {
                oldLyr = new VariableDefinition(mod.TypeSystem.String); wasProt = new VariableDefinition(mod.TypeSystem.Boolean);
                m.Body.Variables.Add(oldLyr); m.Body.Variables.Add(wasProt);
                Instruction first = m.Body.Instructions[0];
                il.InsertBefore(first, il.Create(OpCodes.Ldarg, pNote)); il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
                il.InsertBefore(first, il.Create(OpCodes.Ldarg, pNote)); il.InsertBefore(first, il.Create(OpCodes.Callvirt, mod.ImportReference(getLyric))); il.InsertBefore(first, il.Create(OpCodes.Stloc, oldLyr));
                il.InsertBefore(first, il.Create(OpCodes.Ldarg, pNote)); il.InsertBefore(first, il.Create(OpCodes.Callvirt, mod.ImportReference(getProt))); il.InsertBefore(first, il.Create(OpCodes.Stloc, wasProt));
            }

            foreach (var ins in m.Body.Instructions.ToList())
                if (ins.OpCode == OpCodes.Ret) { ins.OpCode = OpCodes.Br; ins.Operand = POST; }

            il.Append(POST);
            il.Append(il.Create(OpCodes.Ldarg, pNote)); il.Append(il.Create(OpCodes.Brfalse, END));
            il.Append(il.Create(OpCodes.Ldarg, pLyr));  il.Append(il.Create(OpCodes.Brfalse, END));
            Instruction CONT = il.Create(OpCodes.Ldarg, pLyr);
            if (guard)
            {
                il.Append(il.Create(OpCodes.Ldarg, pLyr)); il.Append(il.Create(OpCodes.Ldloc, oldLyr)); il.Append(il.Create(OpCodes.Call, StrEquals(mod)));
                il.Append(il.Create(OpCodes.Brfalse, CONT));
                il.Append(il.Create(OpCodes.Ldloc, wasProt)); il.Append(il.Create(OpCodes.Brtrue, END));
            }
            il.Append(CONT);
            il.Append(il.Create(OpCodes.Ldarg, pNote)); il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(getLang)));
            il.Append(il.Create(OpCodes.Call, convert));
            il.Append(il.Create(OpCodes.Stloc, ph));
            il.Append(il.Create(OpCodes.Ldloc, ph)); il.Append(il.Create(OpCodes.Brfalse, END));
            if (setProt != null)
            {   // quitar el candado antes de escribir (una nota protegida ignora SetPhonemes)
                il.Append(il.Create(OpCodes.Ldarg, pNote)); il.Append(il.Create(OpCodes.Ldc_I4_0));
                il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(setProt)));
            }
            il.Append(il.Create(OpCodes.Ldarg, pNote)); il.Append(il.Create(OpCodes.Ldloc, ph)); il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ldarg, pNote)); il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(getLang)));
            il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(noteSetPh)));
            il.Append(il.Create(OpCodes.Pop));
            if (setProt != null)
            {
                il.Append(il.Create(OpCodes.Ldarg, pNote)); il.Append(il.Create(OpCodes.Ldc_I4_1));
                il.Append(il.Create(OpCodes.Callvirt, mod.ImportReference(setProt)));
            }
            il.Append(END); il.Append(il.Create(OpCodes.Ldarg, pLyr)); il.Append(il.Create(OpCodes.Call, bridgeEnd)); il.Append(il.Create(OpCodes.Pop));
            il.Append(RET);
            if (hasRet) il.Append(il.Create(OpCodes.Ret));
            log("Inyectado en " + m.DeclaringType.Name + "." + m.Name);
        }

        // v4: Bridge.Begin(note, nextNote) como PRIMERA instrucción del método. nextNote se busca por nombre
        // (puede ser ref, por valor o no existir: en ese caso se pasa null).
        static void InjectBegin(MethodDefinition m, ModuleDefinition mod, MethodReference begin, ParameterDefinition pNote, Action<string> log)
        {
            var il = m.Body.GetILProcessor();
            Instruction first = m.Body.Instructions[0];
            ParameterDefinition pNext = m.Parameters.FirstOrDefault(p => p.Name == "nextNote" && !p.IsOut);
            il.InsertBefore(first, il.Create(OpCodes.Ldarg, pNote));
            if (pNext == null) il.InsertBefore(first, il.Create(OpCodes.Ldnull));
            else
            {
                il.InsertBefore(first, il.Create(OpCodes.Ldarg, pNext));
                if (pNext.ParameterType is ByReferenceType) il.InsertBefore(first, il.Create(OpCodes.Ldind_Ref));
            }
            // v4.5: tercer argumento = la letra (parámetro 2, string); si no hay, null
            ParameterDefinition pLyr = m.Parameters.Count > 1 && m.Parameters[1].ParameterType.FullName == "System.String" ? m.Parameters[1] : null;
            if (pLyr == null) il.InsertBefore(first, il.Create(OpCodes.Ldnull));
            else il.InsertBefore(first, il.Create(OpCodes.Ldarg, pLyr));
            il.InsertBefore(first, il.Create(OpCodes.Call, begin));
            il.InsertBefore(first, il.Create(OpCodes.Pop));
            log("Inyectado (inicio) en " + m.DeclaringType.Name + "." + m.Name + (pNext == null ? " [sin nextNote]" : ""));
        }

        static MethodReference StrEquals(ModuleDefinition mod)
        {
            var r = new MethodReference("Equals", mod.TypeSystem.Boolean, mod.TypeSystem.String) { HasThis = false };
            r.Parameters.Add(new ParameterDefinition(mod.TypeSystem.String));
            r.Parameters.Add(new ParameterDefinition(mod.TypeSystem.String));
            return r;
        }

        // Postfix que llama a Bridge.ConvertNotes(this | primer parámetro) al terminar el método
        // (pista o parte: reconvierte las notas existentes al cambiar de banco de voz).
        static void InjectNotesPostfix(MethodDefinition m, ModuleDefinition mod, MethodReference convertNotes, Action<string> log)
        {
            var il = m.Body.GetILProcessor();
            bool hasRet = m.ReturnType.MetadataType != MetadataType.Void;
            VariableDefinition res = null;
            if (hasRet) { res = new VariableDefinition(m.ReturnType); m.Body.Variables.Add(res); }
            m.Body.InitLocals = true;
            Instruction POST = hasRet ? il.Create(OpCodes.Stloc, res) : il.Create(OpCodes.Nop);
            foreach (var ins in m.Body.Instructions.ToList())
                if (ins.OpCode == OpCodes.Ret) { ins.OpCode = OpCodes.Br; ins.Operand = POST; }
            il.Append(POST);
            il.Append(m.HasThis ? il.Create(OpCodes.Ldarg_0) : il.Create(OpCodes.Ldarg, m.Parameters[0]));
            il.Append(il.Create(OpCodes.Call, convertNotes));
            il.Append(il.Create(OpCodes.Pop));
            if (hasRet) il.Append(il.Create(OpCodes.Ldloc, res));
            il.Append(il.Create(OpCodes.Ret));
            log("Inyectado en " + m.DeclaringType.Name + "." + m.Name);
        }

        // ---------- diagnóstico: firmas de los métodos de letra/fonemas y quién los llama ----------
        // Esp2JpnPatcher.exe --cli dump "ruta\VOCALOID7.dll"   -> escribe Esp2Jpn-dump.txt junto al .exe
        public static string Dump(string dll)
        {
            var sb = new System.Text.StringBuilder();
            string bak = BackupPath(dll);
            string src = File.Exists(bak) ? bak : dll;     // se analiza el original si hay copia
            sb.AppendLine("Archivo analizado: " + src);
            var rp = new ReaderParameters { AssemblyResolver = MakeResolver(Path.GetDirectoryName(Path.GetFullPath(dll)), s => { }), InMemory = true };
            using (var mod = ModuleDefinition.ReadModule(src, rp))
            {
                Func<MethodReference, string> sig = mr => mr.ReturnType.Name + " " + mr.DeclaringType.Name + "." + mr.Name + "(" +
                    string.Join(", ", mr.Parameters.Select(p => (p.ParameterType.IsByReference ? "ref " : "") + p.ParameterType.Name.Replace("&", "") + " " + p.Name).ToArray()) + ")";
                Func<string, bool> interesting = n => n.IndexOf("Lyric", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Phoneme", StringComparison.OrdinalIgnoreCase) >= 0;
                sb.AppendLine("\n== Métodos con Lyric/Phoneme en las clases clave ==");
                foreach (var t in mod.GetTypes())
                    if (t.Name == "WIVSMPhonemeService" || t.Name == "WIVSMNote" || t.Name == "WIVSMNoteExtension")
                        foreach (var m in t.Methods.Where(x => interesting(x.Name)))
                            sb.AppendLine(sig(m));
                // v4.5: dónde se trocea el texto de "Insert Lyrics" (cualquier clase del ensamblado)
                sb.AppendLine("\n== Métodos Insert/Split/Syllable/Token/Divide/Mora/Paste/Lyric en CUALQUIER clase ==");
                string[] keys = { "InsertLyric", "SplitLyric", "Syllab", "Token", "Divide", "Mora", "Paste", "Lyrics" };
                foreach (var t in mod.GetTypes())
                    foreach (var m in t.Methods)
                        if (keys.Any(k => m.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (t.Name.IndexOf("Lyric", StringComparison.OrdinalIgnoreCase) >= 0 && t.Name.IndexOf("Insert", StringComparison.OrdinalIgnoreCase) >= 0))
                            sb.AppendLine(t.FullName + "  " + sig(m));
                sb.AppendLine("\n== Propiedades de WIVSMNote ==");
                var nt = mod.GetType(NoteType);
                if (nt != null) foreach (var pr in nt.Properties) sb.AppendLine(pr.PropertyType.Name + " " + pr.Name);
                sb.AppendLine("\n== Quién llama a SetLyrics*/SetPhonemes* (fuera de sus propias clases) ==");
                foreach (var t in mod.GetTypes())
                    foreach (var m in t.Methods.Where(x => x.HasBody))
                        foreach (var ins in m.Body.Instructions)
                        {
                            var mr = ins.Operand as MethodReference;
                            if (mr == null || (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)) continue;
                            if (mr.Name.StartsWith("SetLyrics") || mr.Name.StartsWith("SetPhonemes") || mr.Name.IndexOf("SplitLyric", StringComparison.OrdinalIgnoreCase) >= 0 || mr.Name.IndexOf("Distribute", StringComparison.OrdinalIgnoreCase) >= 0)
                                sb.AppendLine(t.FullName + "." + m.Name + "  ->  " + sig(mr));
                        }
            }
            return sb.ToString();
        }

        // ---------- restaurar ----------
        public static PatchResult Restore(string dll, bool removeHook, Action<string> log)
        {
            try
            {
                string bak = BackupPath(dll);
                if (!File.Exists(bak)) return Fail("No hay copia de seguridad (" + Path.GetFileName(bak) + ").");
                List<string> running = RunningVocaloid(dll);
                if (running.Count > 0) return Fail(OpenMessage(running, "restaurar el original"));
                File.Copy(bak, dll, true);
                log("DLL original restaurado.");
                File.Delete(bak);
                log("Copia de seguridad eliminada: " + Path.GetFileName(bak));
                if (removeHook)
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(dll));
                    foreach (string f in new[] { HookAsm + ".dll", LegacyHookDll, "Esp2Jpn.ini", "Esp2Jpn.log" })
                    { string p = Path.Combine(dir, f); if (File.Exists(p)) { File.Delete(p); log("Eliminado " + f); } }
                }
                return new PatchResult { Ok = true, Message = "Restaurado. VOCALOID7.dll vuelve a ser el original." };
            }
            catch (UnauthorizedAccessException) { return Fail("Sin permisos. Ejecuta como administrador y cierra VOCALOID7."); }
            catch (Exception e) { return Fail("Error: " + e.Message); }
        }
    }
}
