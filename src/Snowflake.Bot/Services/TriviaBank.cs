using System.Security.Cryptography;

namespace Snowflake.Bot.Services;

public sealed record LocalTriviaQuestion(
    string CategoriaId,
    string Dificultad, // "easy", "medium", "hard"
    int Puntos,
    string PreguntaEs, string CorrectaEs, string[] IncorrectasEs,
    string PreguntaEn, string CorrectaEn, string[] IncorrectasEn,
    string PreguntaPt, string CorrectaPt, string[] IncorrectasPt);

/// <summary>
/// Banco local de preguntas de trivia para el bot en Español, Inglés y Portugués.
/// No depende de APIs externas ni servicios de red.
/// </summary>
public static class TriviaBank
{
    private static readonly List<LocalTriviaQuestion> Preguntas =
    [
        // ================= Cultura General =================
        new(
            "general", "easy", 10,
            "¿Cuál es el río más largo y caudaloso del mundo?", "Río Amazonas", ["Río Nilo", "Río Misisipi", "Río Yangtsé"],
            "What is the longest and widest river in the world?", "Amazon River", ["Nile River", "Mississippi River", "Yangtze River"],
            "Qual é o rio mais longo e volumoso do mundo?", "Rio Amazonas", ["Rio Nilo", "Rio Mississippi", "Rio Yangtzé"]
        ),
        new(
            "general", "easy", 10,
            "¿Cuántos lados tiene un heptágono?", "7", ["6", "8", "9"],
            "How many sides does a heptagon have?", "7", ["6", "8", "9"],
            "Quantos lados tem um heptágono?", "7", ["6", "8", "9"]
        ),
        new(
            "general", "medium", 20,
            "¿En qué año ocurrió la caída del Muro de Berlín?", "1989", ["1991", "1985", "1975"],
            "In which year did the Berlin Wall fall?", "1989", ["1991", "1985", "1975"],
            "Em que ano ocorreu a queda do Muro de Berlim?", "1989", ["1991", "1985", "1975"]
        ),
        new(
            "general", "hard", 30,
            "¿Cuál es el animal nacional de Escocia?", "Unicornio", ["Dragón", "León", "Águila real"],
            "What is the national animal of Scotland?", "Unicorn", ["Dragon", "Lion", "Golden Eagle"],
            "Qual é o animal nacional da Escócia?", "Unicórnio", ["Dragão", "Leão", "Águia Dourada"]
        ),

        // ================= Ciencia y Naturaleza =================
        new(
            "ciencia", "easy", 10,
            "¿Cuál es el planeta más grande de nuestro sistema solar?", "Júpiter", ["Saturno", "Neptuno", "Urano"],
            "What is the largest planet in our solar system?", "Jupiter", ["Saturn", "Neptune", "Uranus"],
            "Qual é o maior planeta do nosso sistema solar?", "Júpiter", ["Saturno", "Netuno", "Urano"]
        ),
        new(
            "ciencia", "easy", 10,
            "¿Cuál es el símbolo químico del oro?", "Au", ["Ag", "Fe", "Go"],
            "What is the chemical symbol for gold?", "Au", ["Ag", "Fe", "Go"],
            "Qual é o símbolo químico do ouro?", "Au", ["Ag", "Fe", "Go"]
        ),
        new(
            "ciencia", "medium", 20,
            "¿Qué gas es el más abundante en la atmósfera terrestre?", "Nitrógeno (N2)", ["Oxígeno (O2)", "Dióxido de carbono (CO2)", "Argón"],
            "What is the most abundant gas in Earth's atmosphere?", "Nitrogen (N2)", ["Oxygen (O2)", "Carbon Dioxide (CO2)", "Argon"],
            "Qual é o gás mais abundante na atmosfera da Terra?", "Nitrogênio (N2)", ["Oxigênio (O2)", "Dióxido de carbono (CO2)", "Argônio"]
        ),
        new(
            "ciencia", "hard", 30,
            "¿Cuál es la velocidad aproximada de la luz en el vacío?", "300,000 km/s", ["150,000 km/s", "500,000 km/s", "1,000,000 km/s"],
            "What is the approximate speed of light in a vacuum?", "300,000 km/s", ["150,000 km/s", "500,000 km/s", "1,000,000 km/s"],
            "Qual é a velocidade aproximada da luz no vácuo?", "300.000 km/s", ["150.000 km/s", "500.000 km/s", "1.000.000 km/s"]
        ),

        // ================= Historia =================
        new(
            "historia", "easy", 10,
            "¿Quién fue el primer presidente de los Estados Unidos?", "George Washington", ["Thomas Jefferson", "Abraham Lincoln", "Benjamin Franklin"],
            "Who was the first president of the United States?", "George Washington", ["Thomas Jefferson", "Abraham Lincoln", "Benjamin Franklin"],
            "Quem foi o primeiro presidente dos Estados Unidos?", "George Washington", ["Thomas Jefferson", "Abraham Lincoln", "Benjamin Franklin"]
        ),
        new(
            "historia", "medium", 20,
            "¿Qué civilización antigua construyó la ciudadela de Machu Picchu?", "Inca", ["Azteca", "Maya", "Muisca"],
            "Which ancient civilization built the citadel of Machu Picchu?", "Inca", ["Aztec", "Maya", "Muisca"],
            "Qual civilização antiga construiu a cidadela de Machu Picchu?", "Inca", ["Asteca", "Maia", "Muisca"]
        ),
        new(
            "historia", "hard", 30,
            "¿En qué año comenzó la Primera Guerra Mundial?", "1914", ["1912", "1918", "1939"],
            "In which year did World War I begin?", "1914", ["1912", "1918", "1939"],
            "Em que ano começou a Primeira Guerra Mundial?", "1914", ["1912", "1918", "1939"]
        ),

        // ================= Geografía =================
        new(
            "geografia", "easy", 10,
            "¿Cuál es la capital de Japón?", "Tokio", ["Kioto", "Osaka", "Hiroshima"],
            "What is the capital of Japan?", "Tokyo", ["Kyoto", "Osaka", "Hiroshima"],
            "Qual é a capital do Japão?", "Tóquio", ["Quioto", "Osaka", "Hiroshima"]
        ),
        new(
            "geografia", "medium", 20,
            "¿Cuál es el país más grande del mundo por superficie terrestre?", "Rusia", ["Canadá", "China", "Estados Unidos"],
            "What is the largest country in the world by land area?", "Russia", ["Canada", "China", "United States"],
            "Qual é o maior país do mundo em área territorial?", "Rússia", ["Canadá", "China", "Estados Unidos"]
        ),
        new(
            "geografia", "hard", 30,
            "¿Cuál es el desierto cálido más grande del mundo?", "Desierto del Sáhara", ["Desierto de Arabia", "Desierto de Gobi", "Desierto de Atacama"],
            "What is the largest hot desert in the world?", "Sahara Desert", ["Arabian Desert", "Gobi Desert", "Atacama Desert"],
            "Qual é o maior deserto quente do mundo?", "Deserto do Saara", ["Deserto da Arábia", "Deserto de Gobi", "Deserto do Atacama"]
        ),

        // ================= Videojuegos =================
        new(
            "videojuegos", "easy", 10,
            "¿Cuál es el nombre del protagonista principal de The Legend of Zelda?", "Link", ["Zelda", "Ganon", "Mario"],
            "What is the name of the main protagonist in The Legend of Zelda?", "Link", ["Zelda", "Ganon", "Mario"],
            "Qual é o nome do protagonista principal de The Legend of Zelda?", "Link", ["Zelda", "Ganon", "Mario"]
        ),
        new(
            "videojuegos", "medium", 20,
            "¿En qué año se lanzó originalmente Minecraft al público?", "2009", ["2011", "2007", "2013"],
            "In which year was Minecraft originally released to the public?", "2009", ["2011", "2007", "2013"],
            "Em que ano o Minecraft foi originalmente lançado ao público?", "2009", ["2011", "2007", "2013"]
        ),
        new(
            "videojuegos", "hard", 30,
            "¿Cómo se llama la inteligencia artificial antagonista en el videojuego Portal?", "GLaDOS", ["SHODAN", "Cortana", "HAL 9000"],
            "What is the name of the antagonist artificial intelligence in Portal?", "GLaDOS", ["SHODAN", "Cortana", "HAL 9000"],
            "Qual é o nome da inteligência artificial antagonista no jogo Portal?", "GLaDOS", ["SHODAN", "Cortana", "HAL 9000"]
        ),

        // ================= Anime & Manga =================
        new(
            "anime", "easy", 10,
            "¿Cuál es el sueño principal de Monkey D. Luffy en One Piece?", "Ser el Rey de los Piratas", ["Ser el mejor espadachín", "Encontrar el All Blue", "Ser Almirante de la Marina"],
            "What is Monkey D. Luffy's main dream in One Piece?", "Become the King of the Pirates", ["Become the greatest swordsman", "Find the All Blue", "Become a Navy Admiral"],
            "Qual é o principal sonho de Monkey D. Luffy em One Piece?", "Ser o Rei dos Piratas", ["Ser o melhor espadachim", "Encontrar o All Blue", "Ser Almirante da Marinha"]
        ),
        new(
            "anime", "medium", 20,
            "¿Cómo se llama el cuaderno de muerte en Death Note?", "Death Note", ["Life Note", "Shinigami Diary", "Book of Shadows"],
            "What is the name of the supernatural notebook in Death Note?", "Death Note", ["Life Note", "Shinigami Diary", "Book of Shadows"],
            "Qual é o nome do caderno sobrenatural em Death Note?", "Death Note", ["Life Note", "Shinigami Diary", "Book of Shadows"]
        ),
        new(
            "anime", "hard", 30,
            "En Hunter x Hunter, ¿cuántos tipos principales de Nen existen?", "6", ["4", "5", "7"],
            "In Hunter x Hunter, how many main Nen categories exist?", "6", ["4", "5", "7"],
            "Em Hunter x Hunter, quantos tipos principais de Nen existem?", "6", ["4", "5", "7"]
        ),

        // ================= Cine y Películas =================
        new(
            "cine", "easy", 10,
            "¿Quién dirigió la famosa película 'Titanic' (1997)?", "James Cameron", ["Steven Spielberg", "Christopher Nolan", "Martin Scorsese"],
            "Who directed the famous movie 'Titanic' (1997)?", "James Cameron", ["Steven Spielberg", "Christopher Nolan", "Martin Scorsese"],
            "Quem dirigiu o famoso filme 'Titanic' (1997)?", "James Cameron", ["Steven Spielberg", "Christopher Nolan", "Martin Scorsese"]
        ),
        new(
            "cine", "medium", 20,
            "¿Qué actor interpretó al Joker en la película 'El Caballero Oscuro' (2008)?", "Heath Ledger", ["Joaquin Phoenix", "Jack Nicholson", "Jared Leto"],
            "Which actor played the Joker in 'The Dark Knight' (2008)?", "Heath Ledger", ["Joaquin Phoenix", "Jack Nicholson", "Jared Leto"],
            "Qual ator interpretou o Coringa no filme 'O Cavaleiro das Trevas' (2008)?", "Heath Ledger", ["Joaquin Phoenix", "Jack Nicholson", "Jared Leto"]
        ),

        // ================= Música =================
        new(
            "musica", "easy", 10,
            "¿Cuántas cuerdas suele tener una guitarra clásica estándar?", "6", ["4", "5", "7"],
            "How many strings does a standard acoustic guitar usually have?", "6", ["4", "5", "7"],
            "Quantas cordas costuma ter um violão clássico padrão?", "6", ["4", "5", "7"]
        ),
        new(
            "musica", "medium", 20,
            "¿A qué legendaria banda de rock británica perteneció Freddie Mercury?", "Queen", ["The Beatles", "Led Zeppelin", "Pink Floyd"],
            "Which legendary British rock band did Freddie Mercury belong to?", "Queen", ["The Beatles", "Led Zeppelin", "Pink Floyd"],
            "A qual lendária banda britânica de rock Freddie Mercury pertenceu?", "Queen", ["The Beatles", "Led Zeppelin", "Pink Floyd"]
        ),

        // ================= Mitología =================
        new(
            "mitologia", "easy", 10,
            "¿Quién era el dios del trueno en la mitología nórdica?", "Thor", ["Odín", "Loki", "Freyr"],
            "Who was the god of thunder in Norse mythology?", "Thor", ["Odin", "Loki", "Freyr"],
            "Quem era o deus do trovão na mitologia nórdica?", "Thor", ["Odin", "Loki", "Freyr"]
        ),
        new(
            "mitologia", "medium", 20,
            "¿Quién era el rey de los dioses del Olimpo en la mitología griega?", "Zeus", ["Poseidón", "Hades", "Apolo"],
            "Who was the king of the Olympian gods in Greek mythology?", "Zeus", ["Poseidon", "Hades", "Apollo"],
            "Quem era o rei dos deuses do Olimpo na mitologia grega?", "Zeus", ["Poseidon", "Hades", "Apolo"]
        ),

        // ================= Deportes =================
        new(
            "deportes", "easy", 10,
            "¿Cuántos jugadores por equipo están en la cancha en un partido de fútbol tradicional?", "11", ["9", "10", "12"],
            "How many players per team are on the field in traditional soccer?", "11", ["9", "10", "12"],
            "Quantos jogadores por time ficam em campo em uma partida de futebol tradicional?", "11", ["9", "10", "12"]
        ),
        new(
            "deportes", "medium", 20,
            "¿Cada cuántos años se celebran los Juegos Olímpicos de verano?", "4 años", ["2 años", "3 años", "5 años"],
            "Every how many years are the Summer Olympic Games held?", "4 years", ["2 years", "3 years", "5 years"],
            "A cada quantos anos são realizados os Jogos Olímpicos de verão?", "4 anos", ["2 anos", "3 anos", "5 anos"]
        )
    ];

    public static TriviaPregunta ObtenerPreguntaAleatoria(string lang, string? categoria = null, string? dificultad = null)
    {
        var lista = Preguntas.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            var catFiltro = NormalizarCategoria(categoria);
            var filtradas = lista.Where(p => p.CategoriaId.Equals(catFiltro, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtradas.Count > 0)
                lista = filtradas;
        }

        if (!string.IsNullOrWhiteSpace(dificultad))
        {
            var difFiltro = NormalizarDificultad(dificultad);
            var filtradas = lista.Where(p => p.Dificultad.Equals(difFiltro, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtradas.Count > 0)
                lista = filtradas;
        }

        var candidatos = lista.ToList();
        var item = candidatos[RandomNumberGenerator.GetInt32(candidatos.Count)];

        var (catNombre, difNombre, pregunta, correcta, incorrectas) = lang switch
        {
            "es" => (
                FormatearCategoriaEs(item.CategoriaId),
                FormatearDificultadEs(item.Dificultad),
                item.PreguntaEs,
                item.CorrectaEs,
                item.IncorrectasEs),
            "pt" => (
                FormatearCategoriaPt(item.CategoriaId),
                FormatearDificultadPt(item.Dificultad),
                item.PreguntaPt,
                item.CorrectaPt,
                item.IncorrectasPt),
            _ => (
                FormatearCategoriaEn(item.CategoriaId),
                FormatearDificultadEn(item.Dificultad),
                item.PreguntaEn,
                item.CorrectaEn,
                item.IncorrectasEn)
        };

        var opciones = new List<string> { correcta };
        opciones.AddRange(incorrectas);

        // Mezclar aleatoriamente
        int n = opciones.Count;
        while (n > 1)
        {
            n--;
            int k = RandomNumberGenerator.GetInt32(n + 1);
            (opciones[k], opciones[n]) = (opciones[n], opciones[k]);
        }

        int indiceCorrecto = opciones.IndexOf(correcta);
        return new TriviaPregunta(catNombre, difNombre, pregunta, opciones, indiceCorrecto, item.Puntos);
    }

    private static string NormalizarCategoria(string c)
    {
        var s = c.ToLowerInvariant().Trim();
        if (s.Contains("general") || s.Contains("cultura")) return "general";
        if (s.Contains("ciencia") || s.Contains("science")) return "ciencia";
        if (s.Contains("historia") || s.Contains("history")) return "historia";
        if (s.Contains("geograf") || s.Contains("geo")) return "geografia";
        if (s.Contains("videojuego") || s.Contains("game") || s.Contains("gaming")) return "videojuegos";
        if (s.Contains("anime") || s.Contains("manga")) return "anime";
        if (s.Contains("cine") || s.Contains("pelicula") || s.Contains("film") || s.Contains("movie")) return "cine";
        if (s.Contains("musica") || s.Contains("music")) return "musica";
        if (s.Contains("mitolog") || s.Contains("myth")) return "mitologia";
        if (s.Contains("deporte") || s.Contains("sport") || s.Contains("futbol")) return "deportes";
        return s;
    }

    private static string NormalizarDificultad(string d)
    {
        var s = d.ToLowerInvariant().Trim();
        if (s is "easy" or "facil") return "easy";
        if (s is "hard" or "dificil") return "hard";
        return "medium";
    }

    private static string FormatearCategoriaEs(string id) => id switch
    {
        "general" => "Cultura General",
        "ciencia" => "Ciencia y Naturaleza",
        "historia" => "Historia",
        "geografia" => "Geografía",
        "videojuegos" => "Videojuegos",
        "anime" => "Anime y Manga",
        "cine" => "Cine y Películas",
        "musica" => "Música",
        "mitologia" => "Mitología",
        "deportes" => "Deportes",
        _ => "Cultura General"
    };

    private static string FormatearCategoriaEn(string id) => id switch
    {
        "general" => "General Knowledge",
        "ciencia" => "Science & Nature",
        "historia" => "History",
        "geografia" => "Geography",
        "videojuegos" => "Video Games",
        "anime" => "Anime & Manga",
        "cine" => "Cinema & Movies",
        "musica" => "Music",
        "mitologia" => "Mythology",
        "deportes" => "Sports",
        _ => "General Knowledge"
    };

    private static string FormatearCategoriaPt(string id) => id switch
    {
        "general" => "Conhecimento Geral",
        "ciencia" => "Ciência e Natureza",
        "historia" => "História",
        "geografia" => "Geografia",
        "videojuegos" => "Videogames",
        "anime" => "Anime e Mangá",
        "cine" => "Cinema e Filmes",
        "musica" => "Música",
        "mitologia" => "Mitologia",
        "deportes" => "Esportes",
        _ => "Conhecimento Geral"
    };

    private static string FormatearDificultadEs(string d) => d switch
    {
        "easy" => "Fácil",
        "hard" => "Difícil",
        _ => "Media"
    };

    private static string FormatearDificultadEn(string d) => d switch
    {
        "easy" => "Easy",
        "hard" => "Hard",
        _ => "Medium"
    };

    private static string FormatearDificultadPt(string d) => d switch
    {
        "easy" => "Fácil",
        "hard" => "Difícil",
        _ => "Média"
    };
}
