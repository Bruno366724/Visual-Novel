using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string raizWeb = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
string carpetaImagenes = Path.Combine(raizWeb, "img");
string rutaHistoria = Path.Combine(app.Environment.ContentRootPath, "historia.json");
string rutaEstilo = Path.Combine(app.Environment.ContentRootPath, "estilo.json");

JsonSerializerOptions opcionesJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
opcionesJson.WriteIndented = true;

Directory.CreateDirectory(carpetaImagenes);

// Si todavía no existe la historia guardada, se crea una de ejemplo
if (!File.Exists(rutaHistoria))
{
    GuardarHistoria(rutaHistoria, CrearHistoriaEjemplo(), opcionesJson);
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Devuelve la historia completa
app.MapGet("/api/historia", () =>
{
    List<Escena> lista = LeerHistoria(rutaHistoria, opcionesJson);
    return Results.Ok(lista);
});

// Guarda la historia completa (la manda el editor)
app.MapPut("/api/historia", (List<Escena> lista) =>
{
    if (lista == null || lista.Count == 0)
    {
        return Results.BadRequest(new { error = "La historia necesita al menos una escena." });
    }
    Normalizar(lista);
    GuardarHistoria(rutaHistoria, lista, opcionesJson);
    return Results.Ok(new { guardado = true });
});

// Devuelve el estilo de la caja de texto
app.MapGet("/api/estilo", () =>
{
    Estilo estilo = LeerEstilo(rutaEstilo, opcionesJson);
    return Results.Ok(estilo);
});

// Guarda el estilo de la caja de texto
app.MapPut("/api/estilo", (Estilo estilo) =>
{
    File.WriteAllText(rutaEstilo, JsonSerializer.Serialize(estilo, opcionesJson));
    return Results.Ok(new { guardado = true });
});

// Lista los nombres de las imágenes que hay en wwwroot/img
app.MapGet("/api/imagenes", () =>
{
    List<string> nombres = new List<string>();
    string[] archivos = Directory.GetFiles(carpetaImagenes);
    for (int i = 0; i < archivos.Length; i++)
    {
        string nombre = Path.GetFileName(archivos[i]);
        if (ExtensionValida(nombre))
        {
            nombres.Add(nombre);
        }
    }
    nombres.Sort();
    return Results.Ok(nombres);
});

// Sube una imagen nueva a wwwroot/img
app.MapPost("/api/imagen", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "No llegó ningún archivo." });
    }

    IFormCollection form = await request.ReadFormAsync();
    if (form.Files.Count == 0)
    {
        return Results.BadRequest(new { error = "No llegó ningún archivo." });
    }

    IFormFile archivo = form.Files[0];
    if (archivo.Length == 0 || archivo.Length > 10 * 1024 * 1024)
    {
        return Results.BadRequest(new { error = "La imagen debe pesar menos de 10 MB." });
    }

    string nombreOriginal = Path.GetFileName(archivo.FileName);
    if (!ExtensionValida(nombreOriginal))
    {
        return Results.BadRequest(new { error = "Formato no permitido. Usá png, jpg, gif, webp o svg." });
    }

    string nombre = LimpiarNombre(nombreOriginal);
    string extension = Path.GetExtension(nombre);
    string baseNombre = Path.GetFileNameWithoutExtension(nombre);
    if (baseNombre == "")
    {
        baseNombre = "imagen";
    }
    nombre = baseNombre + extension;

    // Si ya existe una imagen con ese nombre, se agrega un número
    string destino = Path.Combine(carpetaImagenes, nombre);
    int contador = 1;
    while (File.Exists(destino))
    {
        nombre = baseNombre + "_" + contador + extension;
        destino = Path.Combine(carpetaImagenes, nombre);
        contador++;
    }

    using (FileStream flujo = new FileStream(destino, FileMode.Create))
    {
        await archivo.CopyToAsync(flujo);
    }

    return Results.Ok(new { nombre = nombre });
});

app.Run();

// ---------------------------------------------------------------
// Funciones auxiliares
// ---------------------------------------------------------------
static bool ExtensionValida(string nombre)
{
    string extension = Path.GetExtension(nombre).ToLowerInvariant();
    string[] permitidas = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };
    for (int i = 0; i < permitidas.Length; i++)
    {
        if (permitidas[i] == extension)
        {
            return true;
        }
    }
    return false;
}

static string LimpiarNombre(string nombre)
{
    StringBuilder resultado = new StringBuilder();
    for (int i = 0; i < nombre.Length; i++)
    {
        char c = nombre[i];
        if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
        {
            resultado.Append(c);
        }
        else
        {
            resultado.Append('_');
        }
    }
    return resultado.ToString();
}

static void Normalizar(List<Escena> lista)
{
    for (int i = 0; i < lista.Count; i++)
    {
        if (lista[i].Opciones == null)
        {
            lista[i].Opciones = new List<Opcion>();
        }
        if (lista[i].Personajes == null)
        {
            lista[i].Personajes = new List<PersonajeEscena>();
        }

        // Migración: el campo viejo "personaje" pasa a la lista nueva
        if (lista[i].Personajes.Count == 0 && lista[i].Personaje != "")
        {
            PersonajeEscena p = new PersonajeEscena();
            p.Imagen = lista[i].Personaje;
            lista[i].Personajes.Add(p);
        }
        lista[i].Personaje = "";
    }
}

static List<Escena> LeerHistoria(string ruta, JsonSerializerOptions opciones)
{
    List<Escena>? lista = null;
    try
    {
        string json = File.ReadAllText(ruta);
        lista = JsonSerializer.Deserialize<List<Escena>>(json, opciones);
    }
    catch (JsonException)
    {
        lista = null;
    }

    if (lista == null || lista.Count == 0)
    {
        lista = CrearHistoriaEjemplo();
    }
    Normalizar(lista);
    return lista;
}

static Estilo LeerEstilo(string ruta, JsonSerializerOptions opciones)
{
    Estilo? estilo = null;
    if (File.Exists(ruta))
    {
        try
        {
            estilo = JsonSerializer.Deserialize<Estilo>(File.ReadAllText(ruta), opciones);
        }
        catch (JsonException)
        {
            estilo = null;
        }
    }
    if (estilo == null)
    {
        estilo = new Estilo();
    }
    return estilo;
}

static void GuardarHistoria(string ruta, List<Escena> lista, JsonSerializerOptions opciones)
{
    string json = JsonSerializer.Serialize(lista, opciones);
    File.WriteAllText(ruta, json);
}

// ---------------------------------------------------------------
// Historia de ejemplo (solo se usa la primera vez).
// Después todo se edita desde la app y se guarda en historia.json
// ---------------------------------------------------------------
static List<Escena> CrearHistoriaEjemplo()
{
    List<Escena> lista = new List<Escena>();

    Escena e1 = CrearEscena("e1", "fondo_titulo.svg", "", "", "LA PUERTA DEL FARO - Demo");
    AgregarOpcion(e1, "Comenzar", "e2");
    lista.Add(e1);

    Escena e2 = CrearEscena("e2", "fondo_playa.svg", "personaje_marina.svg", "Marina",
        "Llevo tres noches viendo una luz en el faro abandonado. Hoy voy a averiguar qué es.");
    AgregarOpcion(e2, "Entrar al faro", "e4");
    AgregarOpcion(e2, "Esperar al amanecer", "e3");
    lista.Add(e2);

    Escena e3 = CrearEscena("e3", "fondo_playa.svg", "personaje_marina.svg", "Marina",
        "Con el sol, la luz se apagó. Quizás esta noche vuelva a aparecer...");
    AgregarOpcion(e3, "Volver a intentarlo", "e2");
    lista.Add(e3);

    Escena e4 = CrearEscena("e4", "fondo_faro.svg", "personaje_guardian.svg", "Guardián",
        "No esperaba visitas. La luz solo se enciende para quien trae algo que perdió.");
    AgregarOpcion(e4, "Mostrar la llave oxidada", "e5");
    AgregarOpcion(e4, "Decir que solo estaba mirando", "e6");
    lista.Add(e4);

    Escena e5 = CrearEscena("e5", "fondo_faro.svg", "personaje_guardian.svg", "Guardián",
        "La llave encaja. La lámpara gira y todo el mar se ilumina. FINAL: La luz regresa.");
    AgregarOpcion(e5, "Volver al inicio", "e1");
    lista.Add(e5);

    Escena e6 = CrearEscena("e6", "fondo_faro.svg", "personaje_guardian.svg", "Guardián",
        "La lámpara se apaga de golpe y el faro queda a oscuras. FINAL: La oscuridad.");
    AgregarOpcion(e6, "Volver al inicio", "e1");
    lista.Add(e6);

    return lista;
}

static Escena CrearEscena(string id, string fondo, string personaje, string hablante, string texto)
{
    Escena e = new Escena();
    e.Id = id;
    e.Fondo = fondo;
    e.Personaje = personaje;
    e.Hablante = hablante;
    e.Texto = texto;
    e.Opciones = new List<Opcion>();
    return e;
}

static void AgregarOpcion(Escena escena, string texto, string destino)
{
    Opcion o = new Opcion();
    o.Texto = texto;
    o.Destino = destino;
    escena.Opciones.Add(o);
}

class Escena
{
    public string Id { get; set; } = "";
    public string Fondo { get; set; } = "";
    public string Personaje { get; set; } = "";
    public List<PersonajeEscena> Personajes {get; set;} = new List<PersonajeEscena>(); 
    public string Hablante { get; set; } = "";
    public string Texto { get; set; } = "";
    public List<Opcion> Opciones { get; set; } = new List<Opcion>();
}

class PersonajeEscena
{
    public string Imagen { get; set; } = "";
    public double X { get; set; } = 50;       // % horizontal (centro del personaje)
    public double Y { get; set; } = 0;        // % de elevación desde abajo
    public double Escala { get; set; } = 1;   // 1 = tamaño normal
    public bool Espejo { get; set; } = false;
}

class Estilo
{
    public string ColorFondo { get; set; } = "#0a0a19";
    public double Opacidad { get; set; } = 0.88;
    public string ColorBorde { get; set; } = "#ffd166";
    public string ColorTexto { get; set; } = "#ffffff";
    public string ColorNombre { get; set; } = "#ffd166";
    public string ColorTextoNombre { get; set; } = "#111111";
    public double TamanoTexto { get; set; } = 2.3;
    public double Alto { get; set; } = 23;
    public double Redondeo { get; set; } = 1.5;
    public int Velocidad { get; set; } = 25;
    public string Posicion { get; set; } = "abajo";
    public string Fuente { get; set; } = "serif";
    public string ImagenCaja { get; set; } = "";
    public double MargenTexto { get; set; } = 3;
}

class Opcion
{
    public string Texto { get; set; } = "";
    public string Destino { get; set; } = "";
}