<div align="center">

# Esp2Jpn para VOCALOID7

**Parche de fonemas para VOCALOID7: escribe en español y los fonemas X-SAMPA se rellenan solos.** ♪

Esp2JpnPatcher modifica los fonemas de `VOCALOID7.dll` para que, al teclear una letra en español en una nota de un banco japonés, el editor genere los fonemas por ti, con la misma lógica que [Esp2XSAMPA](https://esp2xsampa.nekoweb.org/).

![Versión](https://img.shields.io/badge/versión-1.7-1f6feb)
![Plataforma](https://img.shields.io/badge/Windows-0078d7?logo=windows&logoColor=white)
![VOCALOID7](https://img.shields.io/badge/VOCALOID7-japonés-6f42c1)
![.NET](https://img.shields.io/badge/.NET%20Framework-4.x-512bd4?logo=dotnet&logoColor=white)

</div>

---

## ✨ ¿Qué hace?

Parchea `VOCALOID7.dll` para que, al escribir una letra en español en **una nota** de un banco japonés clásico (idioma de la nota = Japonés), los fonemas X-SAMPA de esa nota se rellenen solos, sin cambiar a mano al *modo de símbolos fonéticos*.

```
hola     →  o 4 a              cantando  →  k a n t a n d o
cielo    →  s j e 4 o          ñu        →  J M
rico     →  4' i k o           tIene     →  t' i e n e
piO      →  p' o               peRRo     →  p e 4 4 4 4 o
```

Solo actúa si la nota es de idioma japonés y la letra es texto latino de una palabra o sílaba. Kana, `[x-sampa]` y cualquier otro texto no se tocan. Si algo falla, el editor se comporta exactamente como antes.

> **Esp2XSAMPA o Esp2JpnPatcher:** Esp2XSAMPA convierte lo que tecleas en cualquier programa, sílaba por sílaba. Esp2JpnPatcher hace la conversión **dentro** de VOCALOID7, sin programa residente ni teclas globales. Usan las mismas reglas.

## 🎹 Características

- **Conversión dentro del editor:** la lógica de Esp2XSAMPA (v2.6) integrada en VOCALOID7.
- **Una palabra por nota:** una palabra o sílaba larga deja todos sus fonemas en la nota donde la escribes (`cantando` → `k a n t a n d o`).
- **Insert Lyrics:** varias palabras separadas por espacio se reparten una por nota.
- **Letra completa:** el parche impide que el motor recorte la letra y la reparta en la nota siguiente.
- **Variantes con mayúsculas:** `U`, `I`, `J`, `G`, `R`, `Z`, `A`, `E`, `O`, igual que en Esp2XSAMPA.
- **Sonido de `ll` / `y`:** zheísmo, sheísmo o yeísmo.
- **Sonido de `z` / `Z`:** tres esquemas; por defecto `z` → `s` y `Z` → `dz`.
- **Nombres de nota del piano roll:** americana (C D E…) o solfeo (Do Re Mi…), con sostenidos o bemoles.
- **Activar / Desactivar** sin restaurar el DLL, y **Restaurar original** desde la copia de seguridad.
- **Ayuda integrada** (menú Ayuda → *Atajos y fonemas*, o `F1`).

## 🚀 Uso rápido

1. **Cierra VOCALOID7 por completo.** Debe estar cerrado al aplicar o restaurar el parche; el programa lo comprueba.
2. Ejecuta `Esp2JpnPatcher.exe` (pide permisos de administrador solo).
3. Pulsa **Examinar…** y elige `VOCALOID7.dll`.
4. Elige el sonido de `ll` / `y`, el de `z` / `Z` y, si quieres, los nombres de nota.
5. Pulsa **Aplicar / re-aplicar** y abre VOCALOID7 con normalidad.
6. Pon una nota con idioma **Japonés**, escribe una palabra en español y los fonemas aparecen solos.

| Botón | Acción |
|---|---|
| **Aplicar / re-aplicar** | Instala el parche (o lo actualiza) con las opciones elegidas |
| **Restaurar original** | Devuelve el DLL original desde `VOCALOID7.dll.esp2jpn.bak` |
| **Activar / Desactivar** | Enciende o apaga la conversión (`enabled=1/0`) sin restaurar |

Si actualizas VOCALOID7, el DLL se reemplaza: vuelve a aplicar el parche.

## 🔤 Reglas principales

Son las mismas que en Esp2XSAMPA. Con minúsculas:

| Escribes | Sale | Nota |
|---|---|---|
| `r`, `l` | `4` | No existe `r` ni `l` en japonés |
| `rr` | `4 4` | |
| `u` | `M` | |
| `f` | `p\` | La `f` no existe en japonés |
| `j`, `ge`, `gi` | `h` | |
| `ñ` | `J` | |
| `z` | `s` | Configurable |
| `ll`, `y` | `Z` (configurable) | `S` o `j` según dialecto |
| `ti`, `ki`, `pi`… | `t' i`, `k' i`, `p' i` | Palatalización ante `i` |
| `i` + vocal | `j` | |

Con mayúsculas (Bloq Mayús o Shift), algunas letras cambian:

| Letra | Efecto | Ejemplo |
|---|---|---|
| `U` | Siempre `M`, nunca `w` | `CUANDO` → `k M a n d o` |
| `I` | Siempre la vocal `i`, nunca `j` | `baIle` → `b a i 4 e` |
| `J`, `G` (de `gi`) | Más fuerte ante `i` (`C`) y ante `u` (`p\ M`) | `JIN` → `C i n`, `JUN` → `p\ M n` |
| `R` | Siempre `rr` | `RO` → `4 4 o` |
| `RR` | Erre fuerte: cuatro `4` | `peRRo` → `p e 4 4 4 4 o` |
| `A E O U` (última vocal tras consonante + `i`) | La `i` se absorbe en la consonante | `piO` → `p' o`, `kiU` → `k' M` |
| `Z` | Variante según el esquema de la `z` | ver abajo |

> **Ojo:** una mayúscula inicial «por costumbre» también cuenta (`Rosa` → `4 4 o s a`, `Zapato` → `dz a p a t o`). Si tus letras llevan mayúscula inicial sin querer, pon `case=0` en `Esp2Jpn.ini`.

### Sonido de `z` y `Z`

| Esquema (`zeta=`) | `za` | `ZA` | Para qué sirve |
|---|---|---|---|
| `sdz` — `z → s`, `Z → dz` *(predeterminado)* | `s a` | `dz a` | La `s` sorda es el sonido real; la `Z` da un ataque más marcado (golpe de «d») |
| `sz` — `z → s`, `Z → z` | `s a` | `z a` | La `Z` da la `z` sonora, más suave y «zumbada» |
| `zdz` — `z → z`, `Z → dz` | `z a` | `dz a` | Fricativa continua con la minúscula; la `s` sorda solo sale con la letra `s` |

## ⚙️ Configuración (`Esp2Jpn.ini`)

El parche guarda sus ajustes en `Esp2Jpn.ini`, junto a `VOCALOID7.dll`, y lo relee al cambiarlo, sin reiniciar nada.

| Clave | Valores | Descripción |
|---|---|---|
| `enabled` | `1` / `0` | Activa o desactiva la conversión |
| `langs` | `0`, `*` o lista (`0,1`) | Idiomas de nota que se convierten (`0` = japonés) |
| `nosplit` | `1` / `0` | `1` = todos los fonemas en la nota donde escribes (predeterminado) |
| `words` | `1` / `0` | `1` = varias palabras (Insert Lyrics) → una por nota (predeterminado) |
| `case` | `1` / `0` | `1` = las mayúsculas tienen significado (predeterminado) |
| `ll` | `Z` / `S` / `j` | Sonido de `ll` y `y` consonántica |
| `zeta` | `sdz` / `sz` / `zdz` | Esquema de la `z` / `Z` |
| `log` | `1` / `0` | Escribe `%LOCALAPPDATA%\Esp2Jpn\Esp2Jpn.log` (diagnóstico) |

`notenames`, `notation` y `accidentals` los usa solo el parcheador para recordar tu elección de nombres de nota.

### Modo consola

```bat
Esp2JpnPatcher.exe --cli patch   "ruta\VOCALOID7.dll" [ll=Z|S|j] [zeta=sdz|sz|zdz] [notes=none|american|solfege] [acc=sharps|flats]
Esp2JpnPatcher.exe --cli restore "ruta\VOCALOID7.dll"
Esp2JpnPatcher.exe --cli status  "ruta\VOCALOID7.dll"
Esp2JpnPatcher.exe --selftest
```

`--selftest` compara el conversor con las salidas esperadas de Esp2XSAMPA.

## 📦 Archivos

Mantén juntos `Esp2JpnPatcher.exe`, `Esp2JpnHook.dll` y `Mono.Cecil.dll`. Al aplicar, el parche copia `Esp2JpnHook.dll` y `Esp2Jpn.ini` junto a `VOCALOID7.dll` y guarda una copia de seguridad como `VOCALOID7.dll.esp2jpn.bak`.

Requiere **Windows** con **.NET Framework 4.x** (incluido en Windows 10 y 11).

## 🛠️ Compilar desde el código

Con Mono (`mcs`):

```bash
sh build.sh      # salida en dist/
```

En Windows, usa `build.bat`. Necesita `Mono.Cecil.dll` (viene en `dist/`).

## ⚠️ Limitaciones

- **Modifica `VOCALOID7.dll`.** Haz tú también una copia del archivo antes de aplicar el parche. Si el DLL tiene firma Authenticode, el parche la invalida.
- **Probado solo en parte:** la lógica del conversor está probada (autotest incluido), pero el parche sobre un `VOCALOID7.dll` real no se ha podido verificar en todas las versiones del editor. Si algo falla, usa **Restaurar original** y avisa.
- Una **actualización de VOCALOID7** reemplaza el DLL: vuelve a aplicar el parche.
- Con el parche activo, las letras en romaji (`ka`, `shi`…) en notas japonesas también se leen como español (`shi` → `s i`). Usa **Desactivar** para escribir romaji.
- Los nombres de nota dependen de encontrar el código esperado en el DLL; si no lo encuentra, no cambia nada y lo avisa. Si tu DLL ya fue modificado con `vocaloid7_notes.py`, restaura primero el original.
- No se tocan otras vías de conversión (por ejemplo, la segmentación G2PA).

## 📝 Registro de cambios

Consulta el [changelog completo](CHANGELOG.md).

## ⚖️ Créditos

Usa [Mono.Cecil](https://github.com/jbevain/cecil) (MIT) para modificar el ensamblado.

---

<div align="center">

Creado por **Liper** · Herramienta de fans, sin relación con YAMAHA ni con los creadores de ningún voicebank. VOCALOID es marca de sus respectivos dueños.

</div>
