namespace Seto90;

/// <summary>
/// Resolucion de archivos binarios del proyecto (PNGs): primero busca dentro del pack
/// (EmbeddedFiles, base64) y despues en disco relativo a la raiz del proyecto.
///
/// Nota de diseno: los juegos de los 90 no tenian "archivos": todo eran offsets dentro de la ROM,
/// y mover un grafico rompia punteros en cadena. Aca la referencia es una ruta relativa legible;
/// en desarrollo vive en disco y al empaquetar viaja adentro del pack — un solo archivo
/// distribuible, sin offsets ni rutas absolutas.
/// </summary>
public static class ImageAssets
{
    public static byte[]? LoadBytes(GameProject project, string? projectRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        if (project.EmbeddedFiles.TryGetValue(relativePath, out var base64))
        {
            try { return Convert.FromBase64String(base64); }
            catch (FormatException) { return null; }
        }
        if (projectRoot != null)
        {
            var path = Path.Combine(projectRoot, relativePath);
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }
        return null;
    }
}
