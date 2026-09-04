/**
 * Banco local de preguntas de trivia para el bot en Español, Inglés y Portugués.
 */

const Preguntas = [
    // ================= Cultura General =================
    {
        categoriaId: "general", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuál es el río más largo y caudaloso del mundo?", correcta: "Río Amazonas", incorrectas: ["Río Nilo", "Río Misisipi", "Río Yangtsé"] },
        en: { pregunta: "What is the longest and widest river in the world?", correcta: "Amazon River", incorrectas: ["Nile River", "Mississippi River", "Yangtze River"] },
        pt: { pregunta: "Qual é o rio mais longo e volumoso do mundo?", correcta: "Rio Amazonas", incorrectas: ["Rio Nilo", "Rio Mississippi", "Rio Yangtzé"] }
    },
    {
        categoriaId: "general", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuántos lados tiene un heptágono?", correcta: "7", incorrectas: ["6", "8", "9"] },
        en: { pregunta: "How many sides does a heptagon have?", correcta: "7", incorrectas: ["6", "8", "9"] },
        pt: { pregunta: "Quantos lados tem um heptágono?", correcta: "7", incorrectas: ["6", "8", "9"] }
    },
    {
        categoriaId: "general", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿En qué año ocurrió la caída del Muro de Berlín?", correcta: "1989", incorrectas: ["1991", "1985", "1975"] },
        en: { pregunta: "In which year did the Berlin Wall fall?", correcta: "1989", incorrectas: ["1991", "1985", "1975"] },
        pt: { pregunta: "Em que ano ocorreu a queda do Muro de Berlim?", correcta: "1989", incorrectas: ["1991", "1985", "1975"] }
    },
    {
        categoriaId: "general", dificultad: "hard", puntos: 30,
        es: { pregunta: "¿Cuál es el animal nacional de Escocia?", correcta: "Unicornio", incorrectas: ["Dragón", "León", "Águila real"] },
        en: { pregunta: "What is the national animal of Scotland?", correcta: "Unicorn", incorrectas: ["Dragon", "Lion", "Golden Eagle"] },
        pt: { pregunta: "Qual é o animal nacional da Escócia?", correcta: "Unicórnio", incorrectas: ["Dragão", "Leão", "Águia Dourada"] }
    },

    // ================= Ciencia y Naturaleza =================
    {
        categoriaId: "ciencia", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuál es el planeta más grande de nuestro sistema solar?", correcta: "Júpiter", incorrectas: ["Saturno", "Neptuno", "Urano"] },
        en: { pregunta: "What is the largest planet in our solar system?", correcta: "Jupiter", incorrectas: ["Saturn", "Neptune", "Uranus"] },
        pt: { pregunta: "Qual é o maior planeta do nosso sistema solar?", correcta: "Júpiter", incorrectas: ["Saturno", "Netuno", "Urano"] }
    },
    {
        categoriaId: "ciencia", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuál es el símbolo químico del oro?", correcta: "Au", incorrectas: ["Ag", "Fe", "Go"] },
        en: { pregunta: "What is the chemical symbol for gold?", correcta: "Au", incorrectas: ["Ag", "Fe", "Go"] },
        pt: { pregunta: "Qual é o símbolo químico do ouro?", correcta: "Au", incorrectas: ["Ag", "Fe", "Go"] }
    },
    {
        categoriaId: "ciencia", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿Qué gas es el más abundante en la atmósfera terrestre?", correcta: "Nitrógeno (N2)", incorrectas: ["Oxígeno (O2)", "Dióxido de carbono (CO2)", "Argón"] },
        en: { pregunta: "What is the most abundant gas in Earth's atmosphere?", correcta: "Nitrogen (N2)", incorrectas: ["Oxygen (O2)", "Carbon Dioxide (CO2)", "Argon"] },
        pt: { pregunta: "Qual é o gás mais abundante na atmosfera da Terra?", correcta: "Nitrogênio (N2)", incorrectas: ["Oxigênio (O2)", "Dióxido de carbono (CO2)", "Argônio"] }
    },
    {
        categoriaId: "ciencia", dificultad: "hard", puntos: 30,
        es: { pregunta: "¿Cuál es la velocidad aproximada de la luz en el vacío?", correcta: "300,000 km/s", incorrectas: ["150,000 km/s", "500,000 km/s", "1,000,000 km/s"] },
        en: { pregunta: "What is the approximate speed of light in a vacuum?", correcta: "300,000 km/s", incorrectas: ["150,000 km/s", "500,000 km/s", "1,000,000 km/s"] },
        pt: { pregunta: "Qual é a velocidade aproximada da luz no vácuo?", correcta: "300.000 km/s", incorrectas: ["150.000 km/s", "500.000 km/s", "1.000.000 km/s"] }
    },

    // ================= Historia =================
    {
        categoriaId: "historia", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Quién fue el primer presidente de los Estados Unidos?", correcta: "George Washington", incorrectas: ["Thomas Jefferson", "Abraham Lincoln", "Benjamin Franklin"] },
        en: { pregunta: "Who was the first president of the United States?", correcta: "George Washington", incorrectas: ["Thomas Jefferson", "Abraham Lincoln", "Benjamin Franklin"] },
        pt: { pregunta: "Quem foi o primeiro presidente dos Estados Unidos?", correcta: "George Washington", incorrectas: ["Thomas Jefferson", "Abraham Lincoln", "Benjamin Franklin"] }
    },
    {
        categoriaId: "historia", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿Qué civilización antigua construyó la ciudadela de Machu Picchu?", correcta: "Inca", incorrectas: ["Azteca", "Maya", "Muisca"] },
        en: { pregunta: "Which ancient civilization built the citadel of Machu Picchu?", correcta: "Inca", incorrectas: ["Aztec", "Maya", "Muisca"] },
        pt: { pregunta: "Qual civilização antiga construiu a cidadela de Machu Picchu?", correcta: "Inca", incorrectas: ["Asteca", "Maia", "Muisca"] }
    },
    {
        categoriaId: "historia", dificultad: "hard", puntos: 30,
        es: { pregunta: "¿En qué año comenzó la Primera Guerra Mundial?", correcta: "1914", incorrectas: ["1912", "1918", "1939"] },
        en: { pregunta: "In which year did World War I begin?", correcta: "1914", incorrectas: ["1912", "1918", "1939"] },
        pt: { pregunta: "Em que ano começou a Primeira Guerra Mundial?", correcta: "1914", incorrectas: ["1912", "1918", "1939"] }
    },

    // ================= Geografía =================
    {
        categoriaId: "geografia", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuál es la capital de Japón?", correcta: "Tokio", incorrectas: ["Kioto", "Osaka", "Hiroshima"] },
        en: { pregunta: "What is the capital of Japan?", correcta: "Tokyo", incorrectas: ["Kyoto", "Osaka", "Hiroshima"] },
        pt: { pregunta: "Qual é a capital do Japão?", correcta: "Tóquio", incorrectas: ["Quioto", "Osaka", "Hiroshima"] }
    },
    {
        categoriaId: "geografia", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿Cuál es el país más grande del mundo por superficie terrestre?", correcta: "Rusia", incorrectas: ["Canadá", "China", "Estados Unidos"] },
        en: { pregunta: "What is the largest country in the world by land area?", correcta: "Russia", incorrectas: ["Canada", "China", "United States"] },
        pt: { pregunta: "Qual é o maior país do mundo em área territorial?", correcta: "Rússia", incorrectas: ["Canadá", "China", "Estados Unidos"] }
    },
    {
        categoriaId: "geografia", dificultad: "hard", puntos: 30,
        es: { pregunta: "¿Cuál es el desierto cálido más grande del mundo?", correcta: "Desierto del Sáhara", incorrectas: ["Desierto de Arabia", "Desierto de Gobi", "Desierto de Atacama"] },
        en: { pregunta: "What is the largest hot desert in the world?", correcta: "Sahara Desert", incorrectas: ["Arabian Desert", "Gobi Desert", "Atacama Desert"] },
        pt: { pregunta: "Qual é o maior deserto quente do mundo?", correcta: "Deserto do Saara", incorrectas: ["Deserto da Arábia", "Deserto de Gobi", "Deserto do Atacama"] }
    },

    // ================= Videojuegos =================
    {
        categoriaId: "videojuegos", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuál es el nombre del protagonista principal de The Legend of Zelda?", correcta: "Link", incorrectas: ["Zelda", "Ganon", "Mario"] },
        en: { pregunta: "What is the name of the main protagonist in The Legend of Zelda?", correcta: "Link", incorrectas: ["Zelda", "Ganon", "Mario"] },
        pt: { pregunta: "Qual é o nome do protagonista principal de The Legend of Zelda?", correcta: "Link", incorrectas: ["Zelda", "Ganon", "Mario"] }
    },
    {
        categoriaId: "videojuegos", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿En qué año se lanzó originalmente Minecraft al público?", correcta: "2009", incorrectas: ["2011", "2007", "2013"] },
        en: { pregunta: "In which year was Minecraft originally released to the public?", correcta: "2009", incorrectas: ["2011", "2007", "2013"] },
        pt: { pregunta: "Em que ano o Minecraft foi originalmente lançado ao público?", correcta: "2009", incorrectas: ["2011", "2007", "2013"] }
    },
    {
        categoriaId: "videojuegos", dificultad: "hard", puntos: 30,
        es: { pregunta: "¿Cómo se llama la inteligencia artificial antagonista en el videojuego Portal?", correcta: "GLaDOS", incorrectas: ["SHODAN", "Cortana", "HAL 9000"] },
        en: { pregunta: "What is the name of the antagonist artificial intelligence in Portal?", correcta: "GLaDOS", incorrectas: ["SHODAN", "Cortana", "HAL 9000"] },
        pt: { pregunta: "Qual é o nome da inteligência artificial antagonista no jogo Portal?", correcta: "GLaDOS", incorrectas: ["SHODAN", "Cortana", "HAL 9000"] }
    },

    // ================= Anime & Manga =================
    {
        categoriaId: "anime", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuál es el sueño principal de Monkey D. Luffy en One Piece?", correcta: "Ser el Rey de los Piratas", incorrectas: ["Ser el mejor espadachín", "Encontrar el All Blue", "Ser Almirante de la Marina"] },
        en: { pregunta: "What is Monkey D. Luffy's main dream in One Piece?", correcta: "Become the King of the Pirates", incorrectas: ["Become the greatest swordsman", "Find the All Blue", "Become a Navy Admiral"] },
        pt: { pregunta: "Qual é o principal sonho de Monkey D. Luffy em One Piece?", correcta: "Ser o Rei dos Piratas", incorrectas: ["Ser o melhor espadachim", "Encontrar o All Blue", "Ser Almirante da Marinha"] }
    },
    {
        categoriaId: "anime", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿Cómo se llama el cuaderno sobrenatural en Death Note?", correcta: "Death Note", incorrectas: ["Life Note", "Shinigami Diary", "Book of Shadows"] },
        en: { pregunta: "What is the name of the supernatural notebook in Death Note?", correcta: "Death Note", incorrectas: ["Life Note", "Shinigami Diary", "Book of Shadows"] },
        pt: { pregunta: "Qual é o nome do caderno sobrenatural em Death Note?", correcta: "Death Note", incorrectas: ["Life Note", "Shinigami Diary", "Book of Shadows"] }
    },
    {
        categoriaId: "anime", dificultad: "hard", puntos: 30,
        es: { pregunta: "En Hunter x Hunter, ¿cuántos tipos principales de Nen existen?", correcta: "6", incorrectas: ["4", "5", "7"] },
        en: { pregunta: "In Hunter x Hunter, how many main Nen categories exist?", correcta: "6", incorrectas: ["4", "5", "7"] },
        pt: { pregunta: "Em Hunter x Hunter, quantos tipos principais de Nen existem?", correcta: "6", incorrectas: ["4", "5", "7"] }
    },

    // ================= Cine y Películas =================
    {
        categoriaId: "cine", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Quién dirigió la famosa película 'Titanic' (1997)?", correcta: "James Cameron", incorrectas: ["Steven Spielberg", "Christopher Nolan", "Martin Scorsese"] },
        en: { pregunta: "Who directed the famous movie 'Titanic' (1997)?", correcta: "James Cameron", incorrectas: ["Steven Spielberg", "Christopher Nolan", "Martin Scorsese"] },
        pt: { pregunta: "Quem dirigiu o famoso filme 'Titanic' (1997)?", correcta: "James Cameron", incorrectas: ["Steven Spielberg", "Christopher Nolan", "Martin Scorsese"] }
    },
    {
        categoriaId: "cine", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿Qué actor interpretó al Joker en la película 'El Caballero Oscuro' (2008)?", correcta: "Heath Ledger", incorrectas: ["Joaquin Phoenix", "Jack Nicholson", "Jared Leto"] },
        en: { pregunta: "Which actor played the Joker in 'The Dark Knight' (2008)?", correcta: "Heath Ledger", incorrectas: ["Joaquin Phoenix", "Jack Nicholson", "Jared Leto"] },
        pt: { pregunta: "Qual ator interpretou o Coringa no filme 'O Cavaleiro das Trevas' (2008)?", correcta: "Heath Ledger", incorrectas: ["Joaquin Phoenix", "Jack Nicholson", "Jared Leto"] }
    },

    // ================= Música =================
    {
        categoriaId: "musica", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuántas cuerdas suele tener una guitarra clásica estándar?", correcta: "6", incorrectas: ["4", "5", "7"] },
        en: { pregunta: "How many strings does a standard acoustic guitar usually have?", correcta: "6", incorrectas: ["4", "5", "7"] },
        pt: { pregunta: "Quantas cordas costuma ter um violão clássico padrão?", correcta: "6", incorrectas: ["4", "5", "7"] }
    },
    {
        categoriaId: "musica", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿A qué legendaria banda de rock británica perteneció Freddie Mercury?", correcta: "Queen", incorrectas: ["The Beatles", "Led Zeppelin", "Pink Floyd"] },
        en: { pregunta: "Which legendary British rock band did Freddie Mercury belong to?", correcta: "Queen", incorrectas: ["The Beatles", "Led Zeppelin", "Pink Floyd"] },
        pt: { pregunta: "A qual lendária banda britânica de rock Freddie Mercury pertenceu?", correcta: "Queen", incorrectas: ["The Beatles", "Led Zeppelin", "Pink Floyd"] }
    },

    // ================= Mitología =================
    {
        categoriaId: "mitologia", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Quién era el dios del trueno en la mitología nórdica?", correcta: "Thor", incorrectas: ["Odín", "Loki", "Freyr"] },
        en: { pregunta: "Who was the god of thunder in Norse mythology?", correcta: "Thor", incorrectas: ["Odin", "Loki", "Freyr"] },
        pt: { pregunta: "Quem era o deus do trovão na mitologia nórdica?", correcta: "Thor", incorrectas: ["Odin", "Loki", "Freyr"] }
    },
    {
        categoriaId: "mitologia", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿Quién era el rey de los dioses del Olimpo en la mitología griega?", correcta: "Zeus", incorrectas: ["Poseidón", "Hades", "Apolo"] },
        en: { pregunta: "Who was the king of the Olympian gods in Greek mythology?", correcta: "Zeus", incorrectas: ["Poseidon", "Hades", "Apollo"] },
        pt: { pregunta: "Quem era o rei dos deuses do Olimpo na mitologia grega?", correcta: "Zeus", incorrectas: ["Poseidon", "Hades", "Apolo"] }
    },

    // ================= Deportes =================
    {
        categoriaId: "deportes", dificultad: "easy", puntos: 10,
        es: { pregunta: "¿Cuántos jugadores por equipo están en la cancha en un partido de fútbol tradicional?", correcta: "11", incorrectas: ["9", "10", "12"] },
        en: { pregunta: "How many players per team are on the field in traditional soccer?", correcta: "11", incorrectas: ["9", "10", "12"] },
        pt: { pregunta: "Quantos jogadores por time ficam em campo em uma partida de futebol tradicional?", correcta: "11", incorrectas: ["9", "10", "12"] }
    },
    {
        categoriaId: "deportes", dificultad: "medium", puntos: 20,
        es: { pregunta: "¿Cada cuántos años se celebran los Juegos Olímpicos de verano?", correcta: "4 años", incorrectas: ["2 años", "3 años", "5 años"] },
        en: { pregunta: "Every how many years are the Summer Olympic Games held?", correcta: "4 years", incorrectas: ["2 years", "3 years", "5 years"] },
        pt: { pregunta: "A cada quantos anos são realizados os Jogos Olímpicos de verão?", correcta: "4 anos", incorrectas: ["2 anos", "3 anos", "5 anos"] }
    }
];

export function getRandomQuestion(lang = 'en', categoria = null, dificultad = null) {
    let list = [...Preguntas];

    if (categoria) {
        const catNorm = normalizeCategory(categoria);
        const filtered = list.filter(p => p.categoriaId.toLowerCase() === catNorm.toLowerCase());
        if (filtered.length > 0) list = filtered;
    }

    if (dificultad) {
        const difNorm = normalizeDifficulty(dificultad);
        const filtered = list.filter(p => p.dificultad.toLowerCase() === difNorm.toLowerCase());
        if (filtered.length > 0) list = filtered;
    }

    const item = list[Math.floor(Math.random() * list.length)];
    const localeKey = lang === 'es' ? 'es' : (lang === 'pt' ? 'pt' : 'en');
    const content = item[localeKey] || item.en;

    const options = [content.correcta, ...content.incorrectas];
    // Shuffle options
    for (let i = options.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [options[i], options[j]] = [options[j], options[i]];
    }

    const correctIndex = options.indexOf(content.correcta);

    return {
        categoriaId: item.categoriaId,
        categoriaNombre: formatCategory(item.categoriaId, lang),
        dificultadId: item.dificultad,
        dificultadNombre: formatDifficulty(item.dificultad, lang),
        pregunta: content.pregunta,
        opciones: options,
        correctIndex,
        correctText: content.correcta,
        puntos: item.puntos
    };
}

function normalizeCategory(c) {
    const s = String(c).toLowerCase().trim();
    if (s.includes('general') || s.includes('cultura')) return 'general';
    if (s.includes('ciencia') || s.includes('science')) return 'ciencia';
    if (s.includes('historia') || s.includes('history')) return 'historia';
    if (s.includes('geograf') || s.includes('geo')) return 'geografia';
    if (s.includes('videojuego') || s.includes('game') || s.includes('gaming')) return 'videojuegos';
    if (s.includes('anime') || s.includes('manga')) return 'anime';
    if (s.includes('cine') || s.includes('pelicula') || s.includes('film') || s.includes('movie')) return 'cine';
    if (s.includes('musica') || s.includes('music')) return 'musica';
    if (s.includes('mitolog') || s.includes('myth')) return 'mitologia';
    if (s.includes('deporte') || s.includes('sport') || s.includes('futbol')) return 'deportes';
    return s;
}

function normalizeDifficulty(d) {
    const s = String(d).toLowerCase().trim();
    if (s === 'easy' || s === 'facil') return 'easy';
    if (s === 'hard' || s === 'dificil') return 'hard';
    return 'medium';
}

function formatCategory(id, lang) {
    const map = {
        general: { es: 'Cultura General', en: 'General Knowledge', pt: 'Conhecimento Geral' },
        ciencia: { es: 'Ciencia y Naturaleza', en: 'Science & Nature', pt: 'Ciência e Natureza' },
        historia: { es: 'Historia', en: 'History', pt: 'História' },
        geografia: { es: 'Geografía', en: 'Geography', pt: 'Geografia' },
        videojuegos: { es: 'Videojuegos', en: 'Video Games', pt: 'Videogames' },
        anime: { es: 'Anime y Manga', en: 'Anime & Manga', pt: 'Anime e Mangá' },
        cine: { es: 'Cine y Películas', en: 'Cinema & Movies', pt: 'Cinema e Filmes' },
        musica: { es: 'Música', en: 'Music', pt: 'Música' },
        mitologia: { es: 'Mitología', en: 'Mythology', pt: 'Mitologia' },
        deportes: { es: 'Deportes', en: 'Sports', pt: 'Esportes' }
    };
    return (map[id] && map[id][lang]) || (map[id] && map[id].en) || 'General Knowledge';
}

function formatDifficulty(d, lang) {
    const map = {
        easy: { es: 'Fácil', en: 'Easy', pt: 'Fácil' },
        medium: { es: 'Media', en: 'Medium', pt: 'Média' },
        hard: { es: 'Difícil', en: 'Hard', pt: 'Difícil' }
    };
    return (map[d] && map[d][lang]) || (map[d] && map[d].en) || 'Medium';
}

export default {
    getRandomQuestion
};
