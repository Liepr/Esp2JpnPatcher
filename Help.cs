// Ventana "Atajos y fonemas" (menú Ayuda). Adaptada de Esp2XSAMPA v3.0: mismo texto de reglas y mismo
// formato (encabezados, mayúsculas con significado, flechas), cambiando lo que en el parcheador no aplica
// (teclas globales, bandeja, temas, inicio con Windows) por lo que sí existe aquí.
using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Esp2Jpn
{
    static class HelpText
    {
        public static readonly string[] Lines = new string[]
        {
            "Atajos y menús:",
            "    F1  \u2192  abrir esta ventana (menú Ayuda > Atajos y fonemas)",
            "    Esc  \u2192  cerrar esta ventana",
            "",
            "El parche no usa teclas globales: actúa dentro de VOCALOID7 cuando",
            "escribes o pegas una letra. Para activarlo o desactivarlo sin restaurar",
            "el DLL usa el botón Activar / Desactivar (enabled=1 / 0 en Esp2Jpn.ini).",
            "",
            "Cómo se escribe en VOCALOID7:",
            "    La nota debe tener el idioma Japonés (langs=0 en Esp2Jpn.ini).",
            "    Escribe la palabra o sílaba en la letra de la nota: los fonemas",
            "    se rellenan solos y la nota queda con el candado de fonemas.",
            "    No hace falta escribir sílaba por sílaba: toda la palabra o",
            "    sílaba va a UNA sola nota  (cantando \u2192 k a n t a n d o).",
            "    Varias palabras separadas por espacio (Insert Lyrics): una",
            "    palabra por nota, en las notas siguientes.",
            "    Guion y apóstrofe dentro de una palabra solo unen fonemas",
            "    (can-tan-do \u2192 todo en una nota). Un guion solo (-) prolonga la",
            "    vocal de la nota anterior.",
            "    Si cambias los fonemas a mano (nota con candado) y la letra no",
            "    cambia, no se vuelven a convertir.",
            "    Kana, [x-sampa] y texto que no sea latino no se tocan.",
            "",
            "Con minúsculas:",
            "    r \u2192 4        rr \u2192 4 4        l \u2192 4",
            "    u \u2192 M        f \u2192 p\\        j \u2192 h",
            "    z \u2192 s  siempre   c + e / i \u2192 s  siempre  (ce \u2192 s e)",
            "    sh \u2192 S  siempre  (sha \u2192 S a,  shu \u2192 S M)",
            "    i \u2192 j si forma diptongo, si no i (tiene \u2192 t j e n e)",
            "    k g t d p b m r + i \u2192 con apóstrofe  (ri \u2192 4' i,  ti \u2192 t' i,  Ri \u2192 4 4' i)",
            "    ni \u2192 J i      fi \u2192 p\\' i      (s, z, ch, h, etc.: sin apóstrofe)",
            "    consonante + i + vocal con tilde: la i se absorbe en la",
            "    consonante  (pió \u2192 p' o,  kiú \u2192 k' M,  nió \u2192 J o)",
            "    ll y la y consonante \u2192 Z, S o j, según «Sonido de ll / y»",
            "    (ver más abajo)",
            "",
            "Con mayúsculas (Bloq Mayús o Shift) algunas letras cambian:",
            "",
            "Requiere case=1 en Esp2Jpn.ini (lo predeterminado); con case=0",
            "las mayúsculas se ignoran.",
            "",
            "  U  sale siempre como M, nunca como w, aunque vaya",
            "      junto a otra vocal.",
            "      cuando \u2192 k w a n d o        CUANDO \u2192 k M a n d o",
            "",
            "  I  sale siempre como la vocal i, nunca como j, aunque",
            "      forme diptongo. Útil cuando la j no suena bien.",
            "      baile \u2192 b a j 4 e        baIle \u2192 b a i 4 e",
            "      tiene \u2192 t j e n e        tIene \u2192 t i e n e",
            "",
            "  A E O U  (la última vocal) tras consonante + i: la i se absorbe",
            "      en la consonante, que lleva apóstrofe. Igual que con tilde.",
            "      piO \u2192 p' o        kiU \u2192 k' M        niO \u2192 J o",
            "      Sin cambio con f (p\\) ni con consonantes sin versión con",
            "      apóstrofe (siO \u2192 s j o). Con minúscula sin tilde, normal:",
            "      kiu \u2192 k j M",
            "",
            "  J  (y la G de gi) ante la i sale más fuerte: C en lugar",
            "      de h; y ante la U, p\\ M en lugar de h M. Ante a, e, o",
            "      sigue siendo h.",
            "      jin \u2192 h i n        JIN \u2192 C i n",
            "      jun \u2192 h M n        JUN \u2192 p\\ M n",
            "      ja \u2192 h a           JA \u2192 h a",
            "",
            "  R  sale como rr (4 4 en lugar de 4), para que suene",
            "      fuerte. Es lo mismo que escribir rr.",
            "      ro \u2192 4 o        RO \u2192 4 4 o",
            "      mar \u2192 m a 4        maR \u2192 m a 4 4",
            "",
            "  RR  dos R mayúsculas juntas dan cuatro 4 (4 4 4 4): una erre",
            "      fuertemente pronunciada, más marcada que la R sola.",
            "      RO \u2192 4 4 o        RRO \u2192 4 4 4 4 o",
            "      pero \u2192 p e 4 o        peRRo \u2192 p e 4 4 4 4 o",
            "",
            "  C  ante e o i (Ce, Ci) cambia igual que la Z, según el esquema",
            "      elegido en la ventana (ver Z, más abajo). La c minúscula",
            "      ante e / i siempre da s. Ante a, o, u sigue siendo k, y Ch",
            "      sigue siendo tS.",
            "      Con z \u2192 s,  Z / C \u2192 dz:   ce \u2192 s e   Ce \u2192 dz e   Ci \u2192 dz i",
            "      Con z \u2192 s,  Z / C \u2192 ts:   ce \u2192 s e   Ce \u2192 ts e   Ci \u2192 ts i",
            "      Con z \u2192 s,  Z / C \u2192 tS:   ce \u2192 s e   Ce \u2192 tS e   Ci \u2192 tS i",
            "      Con z \u2192 s,  Z / C \u2192 z:    ce \u2192 s e   Ce \u2192 z e    Ci \u2192 z i",
            "      ca \u2192 k a        Ca \u2192 k a",
            "",
            "  Y  al inicio de una sílaba (ante vocal) sale siempre como j,",
            "      sea cual sea el sonido de ll / y elegido en la ventana.",
            "      Con ll / y \u2192 Z:   yo \u2192 Z o        YO \u2192 j o",
            "      Con ll / y \u2192 S:   yo \u2192 S o        YO \u2192 j o",
            "      Con ll / y \u2192 j:   yo \u2192 j o        YO \u2192 j o",
            "      Al final de la sílaba (hoy, rey) la Y no cambia.",
            "",
            "  Z  cambia a su variante, y con ella la C de Ce / Ci. En la",
            "      ventana se elige entre cuatro esquemas:",
            "",
            "      z \u2192 s,  Z / C \u2192 dz   (predeterminado)",
            "        La s sorda (\u3055) es el sonido real de la z espa\u00f1ola, y se",
            "        mantiene con la z min\u00fascula. La Z may\u00fascula (y la C de",
            "        Ce, Ci) da la dz, con un golpe de \"d\" antes: ataque m\u00e1s",
            "        marcado, \u00fatil a principio de palabra o tras una pausa.",
            "        za \u2192 s a        ZA \u2192 dz a        Ce \u2192 dz e",
            "",
            "      z \u2192 s,  Z / C \u2192 ts",
            "        La s sorda se mantiene con la z min\u00fascula. La Z may\u00fascula",
            "        (y la C de Ce, Ci) da la ts (\u3064), con un golpe de \"t\" antes:",
            "        ataque marcado como la dz, pero sorda, igual que la s.",
            "        \u00datil si la dz suena demasiado sonora.",
            "        za \u2192 s a        ZA \u2192 ts a        Ce \u2192 ts e",
            "",
            "      z \u2192 s,  Z / C \u2192 tS",
            "        La s sorda se mantiene con la z min\u00fascula. La Z may\u00fascula",
            "        (y la C de Ce, Ci) da la tS (\u3061), la misma africada de la",
            "        \"ch\": ataque marcado y sordo, m\u00e1s \"chicheante\" que la ts.",
            "        \u00datil si la ts suena corta o seca.",
            "        za \u2192 s a        ZA \u2192 tS a        Ce \u2192 tS e",
            "",
            "      z \u2192 s,  Z / C \u2192 z",
            "        La s (\u3055) es el sonido real de la z espa\u00f1ola. La z",
            "        sonora (\u3056) suena m\u00e1s suave y \"zumbada\": \u00fatil si la s",
            "        suena seca o se pierde entre vocales. Tambi\u00e9n afecta a",
            "        la C de Ce, Ci.",
            "        za \u2192 s a        ZA \u2192 z a        Ce \u2192 z e",
            "",
            "      El esquema se guarda en zeta= de Esp2Jpn.ini (sdz, sts, sch o sz).",
            "",
            "  Ojo: una mayúscula inicial \"por costumbre\" también cuenta",
            "      (Rosa \u2192 4 4 o s a,  Zapato \u2192 dz a p a t o,  Yo \u2192 j o,",
            "      Cena \u2192 dz e n a). Si tus letras llevan mayúscula inicial sin",
            "      querer, pon case=0.",
            "",
            "Sonido de ll / y:",
            "    Se elige en la ventana (Z, S o j) y se guarda en ll= de",
            "    Esp2Jpn.ini. Afecta a \"ll\" y a la \"y\" consonante (antes de vocal).",
            "    Z (zheísmo):   llave \u2192 Z a b e      yo \u2192 Z o",
            "    S (sheísmo):   llave \u2192 S a b e      yo \u2192 S o",
            "    j (yeísmo):    llave \u2192 j a b e      yo \u2192 j o",
            "    La y sola o al final de palabra no cambia:  y \u2192 i,  hoy \u2192 o j",
            "    Excepción: la Y en mayúscula ante vocal da siempre j (YO \u2192 j o),",
            "    sea cual sea la opción elegida (ver la letra Y más arriba).",
            "",
            "Nombres de nota:",
            "    Opcional: americana (C D E...) o solfeo (Do Re Mi...), con",
            "    sostenidos o bemoles. Cambia el nombre que muestra el cursor del",
            "    piano roll y las etiquetas de octava del teclado. Se aplica al",
            "    pulsar Aplicar / re-aplicar.",
            "",
            "Los ajustes (sonido de ll / y, esquema de z / C, mayúsculas, nombres de",
            "nota, activar / desactivar) se guardan en Esp2Jpn.ini, en la misma",
            "carpeta que VOCALOID7.dll. El parche relee el archivo al cambiarlo,",
            "sin reiniciar nada (salvo los nombres de nota, que van con Aplicar).",
            "",
            "VOCALOID7 debe estar cerrado al aplicar o restaurar el parche; el",
            "programa lo comprueba. Una actualización de VOCALOID7 reemplaza el",
            "DLL: vuelve a aplicar el parche.",
            "",
            "Fonemas del VOCALOID japonés:",
            "    a i M e o",
            "    k g s S z dz Z dZ t ts tS d n J h C p\\ p b m j 4 w N",
            "    con apóstrofe (solo ante i): k' g' N' t' d' p' b' m' 4' p\\'"
        };
    }

    class HelpForm : Form
    {
        RichTextBox rtb;
        Button btnClose;
        Label lblEsc;
        Font bold;

        public HelpForm()
        {
            Text = "Atajos y fonemas";
            ClientSize = new Size(640, 560);
            MinimumSize = new Size(480, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false;
            Font = SystemFonts.MessageBoxFont;
            KeyPreview = true;

            rtb = new RichTextBox
            {
                Left = 8, Top = 8, Width = 624, Height = 502,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true, BackColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle,
                WordWrap = true, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical,
                Font = MonoFont(10f, FontStyle.Regular)
            };
            // En Windows con japonés instalado el RichTextBox cambia solo de fuente (AutoFont) por tramos y muestra la \\ como ¥.
            // UIFonts sin AutoFont lo desactiva: todo el texto usa la fuente indicada.
            rtb.LanguageOption = RichTextBoxLanguageOptions.UIFonts;
            lblEsc = new Label { Text = "Esc: cerrar", Left = 10, Top = 526, Width = 200, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnClose = new Button { Text = "Cerrar", Left = 552, Top = 520, Width = 80, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            Controls.AddRange(new Control[] { rtb, lblEsc, btnClose });

            btnClose.Click += (s, e) => Close();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
            FormClosed += (s, e) => { if (bold != null) bold.Dispose(); };
            Load += (s, e) => { Fill(); btnClose.Focus(); };
        }

        // Fuente monoespaciada instalada (Consolas, o Courier New si no está).
        static Font MonoFont(float size, FontStyle style)
        {
            foreach (string name in new[] { "Consolas", "Lucida Console", "Courier New" })
                using (var ff = TryFamily(name)) if (ff != null) return new Font(ff, size, style, GraphicsUnit.Point);
            return new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Point);
        }

        static FontFamily TryFamily(string name) { try { return new FontFamily(name); } catch (ArgumentException) { return null; } }

        void Format(int start, int len, Font f, Color fore, Color back)
        {
            if (len <= 0) return;
            rtb.Select(start, len);
            if (f != null) rtb.SelectionFont = f;
            if (fore != Color.Empty) rtb.SelectionColor = fore;
            if (back != Color.Empty) rtb.SelectionBackColor = back;
        }

        // Escribe el texto y le da formato: encabezados, letras en mayúscula (C U I J R Y Z) y flechas.
        void Fill()
        {
            bold = new Font(rtb.Font, FontStyle.Bold);
            Color title = Color.FromArgb(0, 102, 190);       // azul legible sobre blanco
            Color accent = Color.FromArgb(0, 120, 215);
            string[] lines = HelpText.Lines;
            rtb.Text = string.Join("\n", lines);
            rtb.Select(0, rtb.TextLength); rtb.SelectionFont = rtb.Font;       // una sola fuente explícita en todo el texto
            int pos = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (Regex.IsMatch(line, @"^\S.*:$")) Format(pos, line.Length, bold, title, Color.Empty);      // encabezado de sección
                Match m = Regex.Match(line, @"^(\s+)(RR|[CUIJRYZ])\s\s");                                         // letra en mayúscula
                if (m.Success) Format(pos + m.Groups[1].Length, m.Groups[2].Length, bold, Color.White, accent);
                foreach (Match a in Regex.Matches(line, "\u2192")) Format(pos + a.Index, 1, null, title, Color.Empty);
                pos += line.Length + 1;                                                                        // + el salto de línea
            }
            rtb.Select(0, 0);
            rtb.ScrollToCaret();
        }
    }
}
