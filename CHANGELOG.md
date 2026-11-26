# Registro de cambios

## v1.7

### ✨ Novedades
- **Vuelve la opción «Sonido de z (minúscula / MAYÚSCULA)»**, ahora con tres esquemas, igual que en Esp2XSAMPA v2.6:
  - `z → s`, `Z → dz` **(nuevo, predeterminado):** `za` → `s a`, `ZA` → `dz a`. La `s` sorda es el sonido real de la `z` española y la `Z` mayúscula da la `dz`, con un golpe de «d» antes: ataque más marcado, útil a principio de palabra o tras una pausa.
  - `z → s`, `Z → z` (el comportamiento de la v1.6.1): `za` → `s a`, `ZA` → `z a`.
  - `z → z`, `Z → dz`: `za` → `z a`, `ZA` → `dz a`.
- El esquema se guarda en `zeta=sdz|sz|zdz` de `Esp2Jpn.ini`, con efecto inmediato si el parche ya está instalado. También se puede pasar en modo consola (`zeta=...`).

### 🔧 Mejoras
- El conversor sigue ahora la lógica de **Esp2XSAMPA v2.6**.
- El menú de ayuda («Atajos y fonemas») incluye la sección de `Z` de Esp2XSAMPA v2.6, con los tres esquemas y ejemplos.
- El autotest cubre los tres esquemas de la `z`.

### ⚠️ Al actualizar
- Hay que **volver a pulsar «Aplicar / re-aplicar»** para que VOCALOID7 use el `Esp2JpnHook.dll` nuevo.
- Si tu `Esp2Jpn.ini` no tiene `zeta=`, se usa `sdz`: ahora la `Z` mayúscula da `dz` en lugar de `z`. Si quieres el comportamiento anterior, elige `z → s`, `Z → z` en la ventana.

## v1.6.1 y anteriores

Resumen de lo que ya incluían las versiones anteriores:

- **Conversión dentro de VOCALOID7** con el g2p de Esp2XSAMPA portado sin cambios de reglas, para notas de idioma japonés.
- **Una palabra por nota («no repartir»):** una sílaba o palabra larga deja todos sus fonemas en la nota donde se escribe.
- **Reparación del reparto de la letra:** el motor ya no recorta la letra (`holaquetal` → `ho` + `laquetal`); la letra completa queda en la nota editada.
- **Insert Lyrics:** varias palabras separadas por espacio van una por nota (`words=1`).
- **Sonido de `ll` / `y`:** zheísmo, sheísmo o yeísmo, desde la ventana.
- **Nombres de nota del piano roll:** americana o solfeo, con sostenidos o bemoles, y vista previa.
- **Comprobación de VOCALOID7 abierto** antes de aplicar o restaurar, también en modo consola.
- **Menú Ayuda → Atajos y fonemas** (`F1`), con las reglas de minúsculas y mayúsculas.
- **Erre fuerte con `RR`** (cuatro `4`), igual que en Esp2XSAMPA v2.5.
- Ícono propio incrustado en el `.exe`, copia de seguridad `VOCALOID7.dll.esp2jpn.bak` y modo consola (`--cli`, `--selftest`).
