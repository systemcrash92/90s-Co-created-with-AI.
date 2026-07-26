using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Seto90;

/// <summary>
/// Registro de herramientas MCP: la superficie completa de autoria del motor.
///
/// Nota de diseno: los JRPG de la epoca daban al equipo de guion un meta-lenguaje para escribir
/// historia sin tocar el motor, pero sin validacion: un puntero roto se descubria jugando. Aca
/// cada herramienta declara su inputSchema completo (la IA no adivina argumentos), cada escritura
/// valida el proyecto entero y se revierte sola si rompe algo.
/// </summary>
public static class ToolRegistry
{
    /// <summary>Lo que el servidor le dice al agente al conectarse (campo `instructions` del
    /// initialize de MCP). Sin esto un agente recibe 57 herramientas y ningun metodo: no sabe
    /// que preguntar primero, en que orden construir, ni que project.json no se edita a mano.
    /// En ingles porque es el idioma de trabajo de los agentes; el contenido del juego va en el
    /// idioma que elija el autor. Deliberadamente corto: es un briefing, no la guia.</summary>
    public const string Instructions = """
        You are co-authoring a 90s-style JRPG with 90s Engine, together with a human who edits
        inside the running game (F1). Both of you write through this same command layer, so your
        changes appear in their game live and one Ctrl+Z undoes both of your work in order.

        ORIENT BEFORE YOU BUILD. Call query.content_graph and project.design first.
        - If the project only holds the starter content (map.inicio, event.guia, one dialogue)
          or design.md is still the blank template, the game has NOT been designed yet. Do not
          start building. Ask the author what game they want: premise, tone, core loop, main
          characters, and how many chapters. Also ask whether they already have a written text
          (a book, a script) to adapt: if so, story.import brings it in as chapters and scenes.
          Write what you agree on back with project.design, and build from there.
        - If it holds real content, read project.design and query.entity before changing anything.

        RULES THAT ARE NOT NEGOTIABLE.
        1. Never hand-edit project.json. Every write goes through these tools.
        2. Check `ok` on EVERY response, not just the last. A failed write is reverted silently,
           and a final project.validate that returns ok proves nothing about earlier calls.
        3. Grey-box first: flat tiles and placeholder sprites until a chapter is playable end to
           end. Art last.
        4. Use batch.apply for anything that spans several entities (a scene is a map + events +
           dialogues that reference each other): one validation, all-or-nothing, one undo.
           batch.preview shows the diff first without touching anything.

        THE LOOP, once you are building: build with these tools -> SEE it with
        playtest.screenshot -> PLAY it with playtest.run (headless and deterministic: same
        script, same result) -> fix. Never report a scene as done without having looked at it.

        BEFORE CALLING A CHAPTER FINISHED: scene.audit on each important page, balance.audit for
        the curve and the economy, quality.audit to close. They return evidence and questions,
        not verdicts about whether something is good; that judgment is yours and the author's.
        Then LEAVE AN EVAL: save the playtest script that walks the chapter, with asserts, as
        <project>/evals/<name>.txt. evals.run replays the whole suite, so from then on any change
        that breaks an earlier chapter shows up by itself. Run it after touching old content.

        Every scene also has a literary expression (the Mirror Book: story.*). A game and its
        novel grow together here; story.scene.sync records that both sides were reconciled.
        """;

    // ---- Sub-schemas reutilizables ----
    static object Str(string desc) => new { type = "string", description = desc };
    static object Int(string desc) => new { type = "integer", description = desc };
    static object Num(string desc) => new { type = "number", description = desc };
    static object Boolean(string desc) => new { type = "boolean", description = desc };
    static object EnumOf(string desc, params string[] values) => new { type = "string", description = desc, @enum = values };
    static object Arr(string desc, object items) => new { type = "array", description = desc, items };
    static object Obj(string desc, object properties, params string[] required) => new { type = "object", description = desc, properties, required };
    static object Schema(object properties, params string[] required) => new { type = "object", properties, required };

    static readonly object CommandItem = Obj("Comando de evento", new
    {
        kind = EnumOf("Tipo de comando", "Dialogue", "Battle", "SetVariable", "GiveItem", "OpenShop", "PlaySong", "TransferPlayer", "Wait", "MoveEvent", "MovePlayer", "PanCamera", "OpenInn", "PlaySfx", "AddPartyMember", "RemovePartyMember", "ShowEmote", "AdvanceTime", "ShowItemGet", "PlayVfx", "ShowFloat", "SetWeather", "GiveMoney", "TakeMoney"),
        targetId = Str("Id del recurso destino (dialogo, combate, variable, item para GiveItem/ShowItemGet, tienda, cancion, sfx, vfx para PlayVfx, mapa, actor para Add/RemovePartyMember, o evento a mover/panear; para PanCamera '' o 'player' vuelve al jugador); Wait, MovePlayer y OpenInn no lo usan"),
        value = Str("Valor extra: 'true/false' para SetVariable, cantidad para GiveItem/ShowItemGet, 'x,y' para TransferPlayer, segundos para Wait/PanCamera (ej '0.8'), pasos para MoveEvent/MovePlayer (ej 'up,up,face:left'; paso bloqueado se saltea), precio para OpenInn (ej '5'), 'icono' o 'icono:segundos' para ShowEmote (iconos: !, ?, zzz, nota, puntos, corazon; no bloquea la cola), 'manana'/'tarde'/'noche'/'+dia' para AdvanceTime (fade + placa de dia; las paginas condicionan con time.dia y time.franja). ShowItemGet entrega el item Y muestra la ceremonia (mundo oscurecido, rayos girando, sprite del item, nombre + descripcion, fanfarria sfx.item_get); Enter continua. PlayVfx dispara un vfx kind impact en el mundo (value = evento ancla, '' o 'player' = el jugador; no bloquea la cola, componer con Wait). ShowFloat muestra un texto flotante que sube y parpadea (targetId = evento ancla o ''/'player'; value = 'texto' o 'texto:#RRGGBB', ej '+6 HP:#82F0A0'; no bloquea; GiveItem ya flota '+N item' solo). SetWeather cambia el clima del mapa actual en runtime (targetId = vfx kind weather, '' = despejar; no bloquea; el clima autorado del mapa vuelve al cambiar de mapa). GiveMoney/TakeMoney: value = monto entero > 0; GiveMoney suma (float '+$N'); TakeMoney cobra y si NO alcanza corta la cola entera con mensaje (el patron maquina/peaje: cobrar ANTES del ritual). Items con precio 0 = clave, la tienda no los compra")
    }, "kind");

    static readonly object StatsObj = Obj("Stats base", new { hp = Int("Puntos de vida"), mp = Int("Puntos magicos"), attack = Int("Ataque"), defense = Int("Defensa"), speed = Int("Velocidad") });
    static readonly object QualityEncounterItem = Obj("Contrato de un combate", new
    {
        battleId = Str("Id del BattleDef"),
        requirement = EnumOf("Relacion con el progreso", "required", "optional", "repeatable"),
        role = EnumOf("Intencion del encuentro", "tutorial", "common", "elite", "boss"),
        minPreparedActions = Int("Minimo de acciones del jugador preparado; -1 = sin limite"),
        maxPreparedActions = Int("Maximo de acciones del jugador preparado; -1 = sin limite"),
        minPreparedHpPercent = Int("Piso de HP total al vencer preparado (0..100); -1 = sin limite")
    }, "battleId");
    static readonly object QualityCanonChoiceItem = Obj("Eleccion canonica de dialogo", new
    {
        dialogueId = Str("Id del dialogo"),
        nodeId = Str("Nodo que ofrece opciones"),
        choiceIndex = Int("Opcion canonica 0-based")
    }, "dialogueId", "nodeId", "choiceIndex");
    static readonly object QualityExpectedLevelItem = Obj("Rango de nivel esperado al terminar el checkpoint", new
    {
        actorId = Str("Actor de la party"),
        min = Int("Nivel minimo"),
        max = Int("Nivel maximo")
    }, "actorId", "min", "max");
    static readonly object QualityExpectedFlagItem = Obj("Flag esperada al terminar el checkpoint", new
    {
        variableId = Str("Id de flag"),
        value = Boolean("Valor esperado")
    }, "variableId", "value");
    static readonly object QualityCheckpointItem = Obj("Checkpoint canonico posterior a sus steps", new
    {
        id = Str("Id estable del hito"),
        label = Str("Nombre humano"),
        eventId = Str("Evento que expresa el beat (opcional si steps no esta vacio)"),
        pageIndex = Int("Pagina exacta 0-based; -1 = todas/activa"),
        battleId = Str("Combate asociado para balance/contrato (opcional)"),
        expectedMapId = Str("Mapa esperado despues de los pasos"),
        expectedMinMoney = Int("Dinero minimo esperado; -1 = sin assert"),
        expectedMaxMoney = Int("Dinero maximo esperado; -1 = sin assert"),
        expectedPartyActorIds = Arr("Party exacta y ordenada esperada", Str("Id de actor")),
        expectedLevels = Arr("Rangos de nivel esperados", QualityExpectedLevelItem),
        expectedFlags = Arr("Flags esperadas", QualityExpectedFlagItem),
        expectedItemIds = Arr("Items que deben estar en inventario", Str("Id de item")),
        runInPlaytest = Boolean("false = el beat alimenta balance/escena pero se omite del guion runtime"),
        steps = Arr("Pasos reales antes de comprobar el hito; vacio + eventId genera event/auto", Str("event/goto/move/interact/auto/choose/assert-*/checkpoint/dump"))
    }, "id");

    // ---- Catalogo ----
    public static object[] List() =>
    [
        Tool("project.set_info", "Define metadatos del juego: id, titulo, mapa/evento inicial, sprite del jugador, tema de UI activo y filtro CRT.", Schema(new
        {
            id = Str("Id del proyecto (minusculas.puntos)"),
            title = Str("Titulo visible del juego"),
            startMapId = Str("Mapa donde inicia la partida"),
            startEventId = Str("Evento de referencia para la posicion inicial del jugador (legado; startX/startY tienen prioridad)"),
            startX = Int("Columna inicial exacta del jugador en startMapId (definir junto con startY; -1 = derivar de startEventId)"),
            startY = Int("Fila inicial exacta del jugador en startMapId"),
            playerSpriteId = Str("Sprite que usa el jugador (crear antes con sprite.create)"),
            uiThemeId = Str("Tema de UI activo (crear antes con uitheme.set)"),
            crtFilter = Boolean("Filtro CRT de tubo en el runtime: curvatura, scanlines y fosforo (default true; F2 lo alterna en vivo)"),
            showDayClock = Boolean("Reloj de mundo en la UI ('DIA 1 · MAÑANA', arriba a la derecha; default false). El tiempo avanza con el comando AdvanceTime"),
            warpTransition = EnumOf("Efecto default de transicion entre mapas (default iris)", "fade", "iris", "spiral"),
            startMoney = Int("Dinero inicial del jugador (default 0); las victorias suman el money del enemigo"),
            titleImage = Str("Portada del titulo: PNG relativo al proyecto (escalado 'cover'); '' = sin imagen. Si hay titleVfxId, el fondo VFX tiene prioridad"),
            titleVfxId = Str("Fondo VIVO del titulo (vfx kind background, crear con vfx.create): reemplaza la portada estatica por un fondo procedural que late; '' = usar titleImage o el marco sobrio"),
            language = EnumOf("Idioma de la UI del MOTOR (comandos de combate, menus, tienda, placa de dia): en (default) o es. NO afecta el contenido: dialogos y nombres van en el idioma que escriba el autor", "en", "es"),
            partyActorIds = Arr("Party inicial: ids de actores existentes (vacio = el primer actor)", Str("Id de actor"))
        })),
        Tool("evals.run", "Corre la SUITE DE EVALS del juego: todos los guiones de `evals/*.txt` (formato de playtest.run) y un veredicto unico. Es la REGRESION DE GAMEPLAY, distinta de los smokes del motor: los smokes verifican que el motor funciona, los evals verifican que ESTE juego sigue siendo jugable despues de cambiarle contenido. Dejar un eval por capitulo terminado; a partir de ahi, cualquier cambio que lo rompa se ve solo. Reporte compacto si todo pasa; donde falla devuelve el primer paso fallido y el estado final.", Schema(new
        {
            only = Str("Correr un solo eval por nombre de archivo sin extension (ej 'cap1_intro'). Vacio = la suite entera")
        })),
        Tool("project.validate", "Valida todo el contenido y devuelve los problemas con fix sugerido.", Schema(new { })),
        Tool("project.design", "LEE o REESCRIBE el documento de diseno (design.md). La regla de oro del motor es GUION PRIMERO: antes de construir nada hay que saber premisa, tono, core loop, personajes, mapa de capitulos, biblia de estilo y convencion de flags. Sin 'text' devuelve el documento actual (leerlo SIEMPRE antes de la primera escritura de una sesion); con 'text' lo reemplaza entero, tipicamente despues de acordarlo hablando con el autor. OJO: es un documento, no contenido validado — no valida referencias, no entra en el undo y no va en batch. La escritura es atomica y deja design.md.bak.", Schema(new
        {
            text = Str("Documento completo en Markdown. Omitir para LEER en vez de escribir. Reemplaza todo el archivo: leer primero y devolver el documento entero editado, nunca un fragmento")
        })),
        Tool("project.audit", "Audita el DISENO sin modificar contenido: alcance del mundo y dialogos, consecuencias de elecciones, uso de variables, paginas sombreadas, contenido huerfano y metricas comparables de combate/economia. Los hallazgos son explicables; no inventa una nota subjetiva de creatividad.", Schema(new
        {
            includeInfo = Boolean("Incluye preguntas de revision no bloqueantes (default true); false devuelve solo warnings probables")
        })),
        Tool("scene.audit", "Audita UNA ESCENA antes de cerrarla: flags y repeticion, coherencia estructural del dialogo, puesta de camara/movimiento, timing, VFX/SFX, game feel y oportunidades de globos/emotes. Devuelve beats/transcripcion y preguntas para que la IA haga el juicio creativo sin fingir que una heuristica puede puntuar el guion. Indicar eventId o storySceneId, no ambos.", Schema(new
        {
            eventId = Str("Evento/cutscene a revisar (audita todas sus paginas, o solo pageId). Excluyente con storySceneId"),
            pageId = Str("Pagina concreta del evento (opcional; requiere eventId). Si el id esta repetido, usar pageIndex"),
            pageIndex = Int("Indice 0-based de una pagina concreta (opcional; requiere eventId y es excluyente con pageId)"),
            storySceneId = Str("Escena del Libro Espejo: agrupa sus links a eventos/dialogos/batallas y compara gameplay con prosa. Excluyente con eventId"),
            includeInfo = Boolean("Incluye oportunidades de pulido no bloqueantes (default true); false devuelve solo riesgos concretos"),
            includeTranscript = Boolean("Incluye la secuencia de comandos, dialogos y prosa para el juicio semantico de la IA (default true)")
        })),
        Tool("balance.audit", "Audita el BALANCE GLOBAL sin escribir: ordena los checkpoints alcanzables, acumula party/niveles/EXP/dinero, compara ataque basico contra preparacion asequible y tactica determinista, revisa one-shots, duracion, rewards, precios, equipo/consumibles/skills dominados y contramedidas de estados. Declara sus supuestos y devuelve preguntas para el juicio de la IA.", Schema(new
        {
            includeInfo = Boolean("Incluye oportunidades y preguntas de tuning no bloqueantes (default true); false devuelve solo riesgos concretos"),
            routeId = Str("Ruta declarada para ordenar eventos/paginas/elecciones; vacio = inferencia global")
        })),
        Tool("quality.audit", "DIRECTOR DE CALIDAD maestro y de solo lectura: combina validacion, assets, diseno, escenas, balance por ruta, contratos de encuentros y playtests de checkpoints. Warning bloquea; info exige juicio. Devuelve un veredicto unico ready_for_pack/needs_review/blocked.", Schema(new
        {
            routeId = Str("Ruta concreta; vacio = canonica o todas las declaradas"),
            runPlaytests = Boolean("Ejecuta los checkpoints dentro del runtime oculto (default false)"),
            includeInfo = Boolean("Incluye decisiones editoriales no bloqueantes (default true)")
        })),
        Tool("quality.plan.set", "Configura el gate global y reemplaza opcionalmente los contratos de encuentros. No crea rutas: usar quality.route.set.", Schema(new
        {
            enforceOnPack = Boolean("true = pack/publish fallan ante warnings del director"),
            runPlaytestsOnPack = Boolean("true = el gate ejecuta tambien la ruta canonica en runtime"),
            auditAllScenes = Boolean("true = quality.audit revisa todas las paginas interactivas; false = solo eventos de las rutas"),
            canonicalRouteId = Str("Id de la ruta principal (puede quedar vacio mientras se construye)"),
            encounters = Arr("Clasificacion y limites intencionales de cada combate", QualityEncounterItem)
        })),
        Tool("quality.route.set", "Crea o reemplaza una ruta canonica/alternativa completa: checkpoints, elecciones y expectativas que quality.audit convierte en asserts reales.", Schema(new
        {
            id = Str("Id estable, ej route.canon"),
            name = Str("Nombre visible"),
            description = Str("Que recorrido representa"),
            canonChoices = Arr("Elecciones exactas de dialogo para no fusionar ramas", QualityCanonChoiceItem),
            checkpoints = Arr("Hitos en orden", QualityCheckpointItem)
        }, "id", "checkpoints")),
        Tool("quality.route.delete", "Borra una ruta de calidad; si era canonica, limpia canonicalRouteId.", Schema(new
        {
            id = Str("Id de ruta existente")
        })),
        Tool("content.delete", "Borra una entidad por tipo e id. Borrar un evento lo quita tambien de los eventIds de sus mapas; borrar un mapa arrastra sus eventos. Si el borrado deja referencias rotas, se revierte solo con el fix sugerido.", Schema(new
        {
            kind = EnumOf("Tipo de entidad", "map", "event", "dialogue", "actor", "item", "enemy", "battle", "skill", "shop", "song", "sprite", "tileset", "uitheme", "sfx", "vfx", "font", "variable"),
            id = Str("Id de la entidad a borrar")
        }, "kind", "id")),
        Tool("map.set_info", "Cambia nombre y/o cancion de un mapa existente SIN tocar los tiles pintados (map.create es destructivo).", Schema(new
        {
            mapId = Str("Mapa a modificar (debe existir)"),
            name = Str("Nuevo nombre visible (opcional)"),
            songId = Str("Nueva cancion de fondo (opcional; '' = silencio)"),
            weatherVfxId = Str("Clima del mapa (opcional; vfx kind weather, ej vfx.lluvia; '' = despejado)")
        }, "mapId")),
        Tool("project.build_pack", "Genera build/game.pack (un solo archivo distribuible, PNGs embebidos).", Schema(new { })),
        Tool("project.asset_report", "Reporta restricciones retro: resolucion, paleta, tiles y PNGs referenciados.", Schema(new { })),
        Tool("project.export_assets", "Genera build/assets.manifest.json con el inventario de assets.", Schema(new { projectRoot = Str("Raiz del proyecto (opcional; por defecto la actual)") })),
        Tool("transaction.undo", "Revierte la ultima escritura de esta sesion MCP.", Schema(new { })),
        Tool("transaction.redo", "Reaplica la escritura revertida.", Schema(new { })),
        Tool("batch.preview", "PREVISUALIZA una lista de escrituras como una sola transaccion, valida el resultado y devuelve un diff semantico por entidad/campos/celdas, pero NO escribe disco, revision, historial ni undo. Devuelve baseRevision: pasarla luego como expectedRevision a batch.apply impide aplicar sobre una version que cambio mientras el humano revisaba.", Schema(new
        {
            expectedRevision = Int("Revision esperada opcional; si el disco ya cambio, falla con stale_preview en vez de calcular sobre otra base"),
            calls = Arr("Llamadas de escritura propuestas, en orden", Obj("Llamada", new
            {
                name = Str("Herramienta de escritura (create/set/delete/paint/fill/define); las mismas permitidas por batch.apply"),
                arguments = Obj("Argumentos de esa herramienta (su propio inputSchema)", new { })
            }, "name"))
        }, "calls")),
        Tool("batch.apply", "Ejecuta una lista de herramientas de ESCRITURA en UNA transaccion: se aplican en memoria en orden, el proyecto se valida UNA vez al final y se guarda UNA vez. Todo o nada: un paso invalido revierte el batch entero (el disco queda intacto). Permite referencias adelantadas dentro del batch. Usar primero batch.preview y devolver su baseRevision como expectedRevision cierra la carrera entre revisar y aplicar. Un batch = UN undo.", Schema(new
        {
            expectedRevision = Int("Revision de base opcional devuelta por batch.preview; si ya cambio, no escribe y devuelve stale_preview"),
            calls = Arr("Llamadas en orden", Obj("Llamada", new
            {
                name = Str("Herramienta de escritura (create/set/delete/paint/fill/define). NO van en batch: lecturas, capturas, build/validate/audit, transaction.undo/redo ni batch.preview/apply anidado"),
                arguments = Obj("Argumentos de esa herramienta (su propio inputSchema)", new { })
            }, "name"))
        }, "calls")),
        Tool("variable.define", "Crea o reemplaza una variable narrativa (flag/numero/texto).", Schema(new
        {
            id = Str("Id de la variable, ej 'flag.acepto_ayuda'"),
            kind = EnumOf("Tipo de variable", "Flag", "Number", "Text"),
            @default = Str("Valor inicial, ej 'false'")
        }, "id")),
        Tool("tileset.create", "Crea o reemplaza un tileset logico (tiles con color, colision y animacion opcional).", Schema(new
        {
            id = Str("Id del tileset"),
            image = Str("Atlas PNG relativo al proyecto (opcional): grilla de celdas de tileSize px en orden de lectura, el id de cada tile es su indice en el atlas. Vacio o sin .png = tiles de color plano"),
            tileSize = Int("Lado del tile en px: 8, 16 o 32 (debe coincidir con render)"),
            animMs = Int("Reloj de animacion del set en ms por paso (default 300); global al tileset: toda el agua late junta, como en SNES"),
            tiles = Arr("Tiles del set", Obj("Tile", new
            {
                id = Int("Numero de tile unico en el set"),
                name = Str("Nombre"),
                solid = Boolean("true = bloquea el paso"),
                color = Str("Color #RRGGBB"),
                frames = Arr("Tile ANIMADO: celdas del atlas que se ciclan al reloj (ej [8,9,10] = agua de 3 cuadros); vacio = estatico. Requiere atlas PNG", Int("Indice de celda del atlas"))
            }, "id", "color"))
        }, "id", "tiles")),
        Tool("map.create", "Crea o reemplaza un mapa relleno con un tile; despues pintar con map.paint_rect.", Schema(new
        {
            id = Str("Id del mapa"),
            name = Str("Nombre visible"),
            tilesetId = Str("Tileset que usa (debe existir)"),
            width = Int("Ancho en tiles"),
            height = Int("Alto en tiles"),
            fillTile = Int("Tile de relleno inicial"),
            songId = Str("Cancion de fondo (opcional, debe existir si se indica)")
        }, "id", "tilesetId", "width", "height")),
        Tool("map.paint_rect", "Pinta un rectangulo de tiles en un mapa existente.", Schema(new
        {
            mapId = Str("Mapa destino"),
            x = Int("Columna inicial"),
            y = Int("Fila inicial"),
            width = Int("Ancho en tiles"),
            height = Int("Alto en tiles"),
            tileId = Int("Tile a pintar (debe existir en el tileset del mapa)"),
            flags = Int("Orientacion 0-7 (default 0 = normal): bits 0-1 = rotacion 0/90/180/270 horaria, bit 2 (=4) = espejo horizontal. Un mismo tile en 8 orientaciones, como Tiled")
        }, "mapId", "x", "y", "tileId")),
        Tool("map.paint_tiles", "Pinta una lista ARBITRARIA de celdas {x,y,tile} en UNA transaccion: el equivalente exacto del stroke del editor, para formas que no son rectangulos. Estricto: una celda fuera del mapa devuelve error con la celda exacta y NO pinta nada.", Schema(new
        {
            mapId = Str("Mapa destino"),
            cells = Arr("Celdas a pintar", Obj("Celda", new
            {
                x = Int("Columna"),
                y = Int("Fila"),
                tile = Int("Tile a pintar (debe existir en el tileset del mapa)")
            }, "x", "y", "tile")),
            flags = Int("Orientacion 0-7 comun a todas las celdas (default 0 = normal): bits 0-1 = rotacion horaria, bit 2 (=4) = espejo horizontal")
        }, "mapId", "cells")),
        Tool("map.flood_fill", "Flood fill 4-conexo: rellena con tileId el area conectada del tile que hay bajo (x,y), en UNA transaccion (el mismo algoritmo que la tecla F del editor). Si el origen ya es tileId devuelve painted=0 sin escribir nada.", Schema(new
        {
            mapId = Str("Mapa destino"),
            x = Int("Columna del punto de origen"),
            y = Int("Fila del punto de origen"),
            tileId = Int("Tile de relleno (debe existir en el tileset del mapa)"),
            flags = Int("Orientacion 0-7 del relleno (default 0 = normal): bits 0-1 = rotacion horaria, bit 2 (=4) = espejo horizontal")
        }, "mapId", "x", "y", "tileId")),
        Tool("map.set_warps", "Define los warps de un mapa (reemplaza la lista completa): pisar la casilla transfiere al jugador al mapa destino con fade y sonido de puerta.", Schema(new
        {
            mapId = Str("Mapa que contiene los warps"),
            warps = Arr("Warps del mapa", Obj("Warp", new
            {
                x = Int("Columna de la casilla warp"),
                y = Int("Fila de la casilla warp"),
                toMapId = Str("Mapa destino (debe existir)"),
                toX = Int("Columna de llegada en el mapa destino"),
                toY = Int("Fila de llegada en el mapa destino"),
                transition = EnumOf("Efecto al cruzar (opcional; vacio = default del proyecto). iris = circulo que se cierra sobre el jugador; spiral = bloques en espiral hacia el centro", "fade", "iris", "spiral")
            }, "x", "y", "toMapId", "toX", "toY"))
        }, "mapId", "warps")),
        Tool("event.create", "Crea o reemplaza un evento (NPC, trigger, objeto o cutscene) en un mapa.", Schema(new
        {
            id = Str("Id del evento"),
            mapId = Str("Mapa donde vive"),
            name = Str("Nombre visible"),
            kind = EnumOf("Tipo de evento", "Npc", "Trigger", "Object", "Cutscene"),
            x = Int("Columna"),
            y = Int("Fila"),
            sprite = Str("Id de sprite (crear antes con sprite.create; vacio = rectangulo placeholder)"),
            routineId = EnumOf("Rutina de movimiento", "idle", "pace_horizontal", "pace_vertical", "look_around", "guard")
        }, "id", "mapId", "x", "y")),
        Tool("event.set_commands", "Define los comandos de la pagina principal de un evento.", Schema(new
        {
            eventId = Str("Evento destino"),
            commands = Arr("Comandos en orden de ejecucion", CommandItem)
        }, "eventId", "commands")),
        Tool("event.set_pages", "Reemplaza todas las paginas condicionadas de un evento (se evaluan de la ultima a la primera).", Schema(new
        {
            eventId = Str("Evento destino"),
            pages = Arr("Paginas", Obj("Pagina de evento", new
            {
                id = Str("Nombre de la pagina"),
                conditions = Arr("Condiciones (todas deben cumplirse)", Obj("Condicion", new { variableId = Str("Variable a comparar"), equalsValue = Str("Valor esperado, ej 'true'") }, "variableId", "equalsValue")),
                commands = Arr("Comandos de la pagina", CommandItem)
            }, "commands"))
        }, "eventId", "pages")),
        Tool("dialogue.create", "Crea o reemplaza un dialogo como grafo de nodos con elecciones y efectos.", Schema(new
        {
            id = Str("Id del dialogo"),
            startNodeId = Str("Nodo inicial"),
            nodes = Arr("Nodos del grafo", Obj("Nodo", new
            {
                id = Str("Id del nodo dentro del dialogo"),
                speaker = Str("Quien habla"),
                text = Str("Texto (soporta acentos y enie)"),
                choices = Arr("Elecciones (opcional)", Obj("Eleccion", new { text = Str("Texto de la opcion"), nextNodeId = Str("Nodo destino") }, "text", "nextNodeId")),
                nextNodeId = Str("Nodo siguiente si no hay elecciones (vacio = fin)"),
                effects = Arr("Efectos al entrar al nodo", CommandItem)
            }, "id", "text"))
        }, "id", "startNodeId", "nodes")),
        Tool("actor.create", "Crea o reemplaza un actor jugable.", Schema(new
        {
            id = Str("Id del actor"),
            name = Str("Nombre"),
            level = Int("Nivel inicial"),
            stats = StatsObj,
            growth = Obj("Crecimiento por nivel al subir (opcional; default del motor: +4HP +1MP +2Atk +1Def +1Vel)", new { hp = Int("HP por nivel"), mp = Int("MP por nivel"), attack = Int("Ataque por nivel"), defense = Int("Defensa por nivel"), speed = Int("Velocidad por nivel") }),
            skillIds = Arr("Skills que sabe (deben existir; crear con skill.create)", Str("Id de skill"))
        }, "id", "name")),
        Tool("skill.create", "Crea o reemplaza una skill de combate. damage: dano = max(1, power + attack/2 - defensa del blanco), elige blanco enemigo. heal: cura power al aliado que elijas (y lo despierta). revive: levanta al caido que elijas con power HP. Consume mpCost.", Schema(new
        {
            id = Str("Id de la skill, ej 'skill.chispa'"),
            name = Str("Nombre visible"),
            mpCost = Int("MP que consume (default 1)"),
            power = Int("Potencia (default 5)"),
            kind = EnumOf("Tipo de skill", "damage", "heal", "revive"),
            status = EnumOf("Estado que aplica ademas del dano (solo kind damage; determinista: siempre pega). poison: -maxHp/8 por turno. sleep: pierde 2 turnos, un golpe despierta.", "poison", "sleep"),
            vfxId = Str("Efecto visual al impactar (vfx kind impact, crear con vfx.create; vacio = default del motor: vfx.hit si damage, vfx.heal si heal/revive)")
        }, "id", "name")),
        Tool("item.create", "Crea o reemplaza un item. Consumible (effect) o equipable (slot + bonus, se pone desde EQUIPO en el menu de pausa).", Schema(new
        {
            id = Str("Id del item"),
            name = Str("Nombre visible"),
            price = Int("Precio en tiendas"),
            effect = Str("Efecto consumible: 'heal:20' (cura 20 HP), 'cure:poison'/'cure:sleep'/'cure:all' (quita estados) o 'revive:15' (levanta a un caido con 15 HP)"),
            slot = EnumOf("Slot equipable (opcional): weapon o armor", "weapon", "armor"),
            bonus = Obj("Stats que suma mientras esta equipado (opcional, requiere slot)", new { hp = Int("HP extra"), mp = Int("MP extra"), attack = Int("Ataque extra"), defense = Int("Defensa extra"), speed = Int("Velocidad extra") }),
            description = Str("Una linea de descripcion, mostrada en la ceremonia de ShowItemGet (opcional)"),
            spriteId = Str("Sprite del item para ShowItemGet (opcional; default: convencion item.X -> sprite.X, y sin sprite se dibuja un destello generico)")
        }, "id", "name")),
        Tool("enemy.create", "Crea o reemplaza un enemigo.", Schema(new
        {
            id = Str("Id del enemigo"),
            name = Str("Nombre visible"),
            stats = StatsObj,
            exp = Int("EXP que otorga"),
            money = Int("Dinero que otorga"),
            inflicts = EnumOf("Estado que aplica su ataque basico (opcional). poison: -maxHp/8 por turno del envenenado. sleep: la victima pierde 2 turnos.", "poison", "sleep")
        }, "id", "name")),
        Tool("battle.create", "Crea o reemplaza un combate por turnos.", Schema(new
        {
            id = Str("Id del combate"),
            view = EnumOf("Vista", "Frontal", "Side"),
            rollingHp = Boolean("HP rodante: el numero baja girando como un odometro, asi un golpe letal deja margen para curarse antes de que termine de caer"),
            enemyIds = Arr("Enemigos (deben existir)", Str("Id de enemigo")),
            victoryFlag = Str("Flag que se activa al ganar (opcional, debe existir)"),
            damageFormula = Str("Formula declarativa, ej 'max(1, attack - defense)'"),
            songId = Str("Cancion de batalla (opcional, debe existir; al terminar vuelve el tema del mapa)"),
            boss = Boolean("Presentacion de jefe: aura pulsante detras del enemigo, placa de nombre al entrar y temblor mas fuerte al recibir golpes (default false)"),
            backgroundVfxId = Str("Fondo de batalla animado por capas (vfx kind background, crear con vfx.create; vacio = fondo plano)")
        }, "id", "enemyIds")),
        Tool("shop.create", "Crea o reemplaza una tienda.", Schema(new
        {
            id = Str("Id de la tienda"),
            name = Str("Nombre visible"),
            itemIds = Arr("Items en venta (deben existir)", Str("Id de item"))
        }, "id", "itemIds")),
        Tool("song.create", "Crea o reemplaza una cancion chiptune por canales estilo tracker (notas con duracion, volumen y envolvente por canal).", Schema(new
        {
            id = Str("Id de la cancion"),
            tempo = Int("Pulsos por minuto (una nota sin ':N' dura un pulso)"),
            channels = Arr("Canales que suenan en paralelo", Obj("Canal", new
            {
                wave = EnumOf("Forma de onda", "square", "triangle", "saw", "noise"),
                notes = Arr("Notas 'C4', 'G#3', 'Bb2'; 'R' = silencio; ':N' = dura N pulsos (ej 'C4:2', 'R:4')", Str("Nota")),
                volume = Num("Volumen del canal 0..1 (default 1; ej 0.5 para el acompanamiento)"),
                attackMs = Int("Ataque de la envolvente en ms (default 10; corto = percusivo)"),
                releaseMs = Int("Liberacion en ms (default 80; corto = staccato, largo = pad)"),
                duty = Num("Ancho de pulso del square 0.05..0.95 (0.5 cuadrada; 0.25 y 0.125 = colores NES)")
            }, "notes"))
        }, "id", "channels")),
        Tool("sprite.create", "Crea o reemplaza un sprite procedural: paleta + poses con filas de pixeles ('.' transparente, digito hex = indice de paleta).", Schema(new
        {
            id = Str("Id del sprite, ej 'sprite.heroe'"),
            name = Str("Nombre descriptivo"),
            width = Int("Ancho en px (default 16, max 64)"),
            height = Int("Alto en px (default 16, max 64)"),
            palette = Arr("Colores #RRGGBB (max 15; el indice 0 es el primer color)", Str("Color")),
            poses = Arr("Poses por direccion. 'down' es obligatoria; 'up' cae a down; left/right se espejan entre si.", Obj("Pose", new
            {
                direction = EnumOf("Direccion", "Down", "Up", "Left", "Right"),
                frames = Arr("Frames de animacion (2 = caminata clasica)", Obj("Frame", new { rows = Arr("Height filas de Width caracteres", Str("Fila, ej '..1AA1..'")) }, "rows"))
            }, "direction", "frames"))
        }, "id", "palette", "poses")),
        Tool("sprite.import_sheet", "Crea un sprite desde un spritesheet PNG del proyecto: 4 filas (down, up, left, right) x N frames.", Schema(new
        {
            id = Str("Id del sprite"),
            name = Str("Nombre descriptivo"),
            image = Str("Ruta PNG relativa al proyecto, ej 'assets/heroe.png'"),
            frameWidth = Int("Ancho de cada frame en px"),
            frameHeight = Int("Alto de cada frame en px"),
            framesPerDirection = Int("Frames por fila/direccion (default 2)")
        }, "id", "image", "frameWidth", "frameHeight")),
        Tool("font.import", "Registra una fuente bitmap desde un PNG con grilla de glifos.", Schema(new
        {
            id = Str("Id de la fuente"),
            image = Str("Ruta PNG relativa al proyecto"),
            glyphWidth = Int("Ancho de celda en px"),
            glyphHeight = Int("Alto de celda en px"),
            charset = Str("Caracteres en orden de lectura de la grilla (izq-der, arriba-abajo)"),
            variableWidth = Boolean("true = recortar cada glifo por columnas usadas (proporcional)")
        }, "id", "image", "charset")),
        Tool("uitheme.set", "Crea o actualiza un tema de UI (colores de ventana, estilo, velocidad de texto) y opcionalmente lo activa.", Schema(new
        {
            id = Str("Id del tema"),
            windowBg = Str("Fondo de ventanas #RRGGBB"),
            windowBorder = Str("Borde de ventanas #RRGGBB"),
            textColor = Str("Color de texto #RRGGBB"),
            accentColor = Str("Color de acento (nombres, seleccion) #RRGGBB"),
            shadowColor = Str("Sombra del texto #RRGGBB"),
            fontId = Str("Fuente importada a usar (vacio = fuente embebida del motor)"),
            style = EnumOf("Estilo de borde", "beveled", "rounded", "plain"),
            textSpeedCps = Int("Velocidad del typewriter en caracteres/segundo (default 40)"),
            makeActive = Boolean("true (default) = pasa a ser el tema activo del proyecto")
        }, "id")),
        Tool("sfx.create", "Crea o reemplaza un efecto de sonido por sintesis (barrido de frecuencia + decaimiento). Ids reservados que sobreescriben defaults del motor: sfx.cursor, sfx.confirm, sfx.cancel, sfx.text_blip, sfx.encounter, sfx.hit, sfx.player_hit, sfx.victory, sfx.save, sfx.door, sfx.item_get.", Schema(new
        {
            id = Str("Id del sfx"),
            wave = EnumOf("Forma de onda", "square", "triangle", "saw", "noise"),
            startFreq = Num("Frecuencia inicial en Hz (20-20000)"),
            endFreq = Num("Frecuencia final en Hz (barrido lineal)"),
            durationMs = Int("Duracion en ms (1-4000)"),
            decay = Num("Decaimiento 0-1 (0 = sostiene, 1 = cae rapido)"),
            volume = Num("Volumen 0-1"),
            duty = Num("Ciclo de trabajo para square, 0.05-0.95 (default 0.5)")
        }, "id")),
        Tool("vfx.create", "Crea o reemplaza un efecto visual generado por codigo (cero PNGs/shaders): el tracker visual simetrico a song.create. kind impact = destello de ataque (timeline corto de capas ancladas a un blanco: se usa desde skill.vfxId, el comando PlayVfx, o pisando los reservados vfx.hit/vfx.heal que todo combate ya usa). kind background = fondo de batalla animado (patrones que ondulan por scanline, scrollean y ciclan colores; se usa desde battle.backgroundVfxId y loopea todo el combate). Determinista: mismo efecto en cualquier maquina, capturable en fase exacta con playtest.screenshot.", Schema(new
        {
            id = Str("Id del vfx, ej 'vfx.corte'. Reservados sobreescribibles: vfx.hit (impacto default de ataques y skills damage), vfx.heal (curas/revives)"),
            kind = EnumOf("Tipo: impact (destello puntual), background (fondo de batalla infinito) o weather (clima de mapa: loopea sobre el mundo; enganchar con map.set_info weatherVfxId o el comando SetWeather; reservados sobreescribibles vfx.lluvia/vfx.niebla/vfx.nieve)", "impact", "background", "weather"),
            durationMs = Int("impact: duracion en ms (100-5000, default 600). background: se ignora. weather: 0/1000 = clima permanente, > 1000 = largo del CICLO en ms (hasta 600000; las capas viven en ventanas startMs/endMs con rampas)"),
            sfxId = Str("[impact] SONIDO del efecto: un vfx es AUDIOVISUAL, el sonido viaja con el y no con quien lo dispara — asi la skill, el ataque basico, el item y el comando PlayVfx suenan bien sin repetir el id en cada uno. Crear antes con sfx.create (o usar un reservado). Vacio = mudo. Prohibido en background/weather: loopean y sonarian un sample por cuadro"),
            layers = Arr("Capas del efecto (1-16), como los canales de una cancion", Obj("Capa", new
            {
                shape = EnumOf("[impact] Primitiva: flash (pantallazo), spark (particulas), ring (onda expansiva), slash (tajo), beam (columna de luz). [weather] rain (trazos que caen, angle = viento en grados, scrollY = velocidad, sizePx = largo), snow (copos lentos que ondulan), fog (bancos de niebla que derivan, spreadPx = radio, scrollX = viento), flash (relampago periodico cada cycleMs >= 1000), splash (salpicaduras contra el piso). CICLOS de clima: durationMs > 1000 = largo del ciclo y cada capa vive en su ventana startMs/endMs con rampas suaves (llueve -> escampa -> repite; ej vfx.tormenta reservado)", "flash", "spark", "ring", "slash", "beam", "rain", "snow", "fog", "splash"),
                pattern = EnumOf("[background] Patron: bands (bandas horizontales), checker (damero), rings (anillos concentricos), waves (columnas verticales)", "bands", "checker", "rings", "waves"),
                color = Str("[impact] Color #RRGGBB de la capa (default #FFFFFF)"),
                colors = Arr("[background] Colores #RRGGBB del patron (1-8), ciclados si cycleMs > 0", Str("Color")),
                motion = EnumOf("[impact spark] Movimiento de las particulas (default burst)", "burst", "rise", "fall", "spiral", "expand"),
                blend = EnumOf("Mezcla: additive = luz (default), normal, multiply = sombra", "additive", "normal", "multiply"),
                startMs = Int("[impact] Inicio de la capa dentro del efecto (default 0)"),
                endMs = Int("[impact] Fin de la capa (0 = hasta durationMs)"),
                count = Int("[impact spark] Cantidad de particulas (1-64, default 8)"),
                spreadPx = Int("[impact] Alcance en px: radio de dispersion/onda o media-longitud del tajo (1-256, default 24)"),
                sizePx = Int("Tamano en px: lado de particula / ancho base del beam (impact, 0 = auto 2) o alto de banda/celda (background, 0 = auto 16)"),
                angle = Num("[impact slash] Angulo del tajo en grados (default 45)"),
                scrollX = Num("[background] Scroll horizontal en px/seg (checker/waves)"),
                scrollY = Num("[background] Scroll vertical en px/seg (bands/checker; en rings = deriva radial)"),
                distortAmp = Num("[background] Amplitud de la ondulacion por scanline en px (0-64; 0 = sin distorsion): cada linea de pantalla se desplaza por su cuenta y el patron respira"),
                distortFreq = Num("[background] Frecuencia de la ondulacion en radianes por linea (default 0.06)"),
                distortSpeed = Num("[background] Velocidad de la ondulacion en radianes/seg (default 1.5)"),
                cycleMs = Int("[background] Palette cycling: los colores rotan cada N ms (0 = estatico, minimo 50)")
            }))
        }, "id", "layers")),
        Tool("story.book.set", "Configura el Libro Espejo: el manuscrito literario que evoluciona junto al juego y puede entregarse a una editorial.", Schema(new
        {
            title = Str("Titulo del libro"),
            subtitle = Str("Subtitulo opcional"),
            author = Str("Nombre o seudonimo del autor"),
            shortTitle = Str("Titulo corto para cabeceras y nombre del archivo"),
            language = Str("Idioma BCP-47, por ejemplo es, es-UY o en"),
            contact = Str("Contacto editorial opcional"),
            pageSize = EnumOf("Tamano del manuscrito DOCX", "letter", "a4"),
            description = Str("Sinopsis general o texto de presentacion del libro")
        })),
        Tool("story.chapter.set", "Crea o actualiza un capitulo del Libro Espejo sin reemplazar sus escenas.", Schema(new
        {
            id = Str("Id estable del capitulo, por ejemplo chapter.la_llegada"),
            title = Str("Titulo literario del capitulo"),
            summary = Str("Resumen de continuidad para el autor y la IA")
        }, "id")),
        Tool("story.scene.set", "Crea o actualiza una escena literaria y sus enlaces canonicos al juego. La prosa no es una transcripcion: adapta la experiencia jugable al lenguaje de novela.", Schema(new
        {
            chapterId = Str("Capitulo que contiene la escena"),
            id = Str("Id global y estable de la escena, por ejemplo scene.encuentro_en_el_faro"),
            title = Str("Titulo de trabajo de la escena"),
            synopsis = Str("Que ocurre y que cambia dramaticamente"),
            pov = Str("Punto de vista, por ejemplo Pauro / tercera limitada"),
            location = Str("Lugar narrativo"),
            time = Str("Momento o franja temporal"),
            status = EnumOf("Estado editorial", "draft", "revised", "final"),
            prose = Str("Prosa completa de la escena; separar parrafos con lineas vacias"),
            tags = Arr("Etiquetas de trama, personajes y tono", Str("Etiqueta")),
            links = Arr("Fuentes del juego que expresan esta escena", Obj("Enlace canonico", new
            {
                kind = EnumOf("Tipo de contenido", "map", "event", "dialogue", "battle", "actor", "item", "enemy", "shop", "song"),
                id = Str("Id exacto de la entidad del juego"),
                role = Str("Funcion narrativa, por ejemplo source, setting, outcome o character")
            }, "kind", "id")),
            canonChoices = Arr("Ruta canonica cuando el juego ramifica; el libro sigue estas elecciones", Obj("Eleccion canonica", new
            {
                dialogueId = Str("Id del dialogo"),
                nodeId = Str("Id del nodo con elecciones"),
                choiceIndex = Int("Indice 0-based de la opcion canonica")
            }, "dialogueId", "nodeId", "choiceIndex"))
        }, "chapterId", "id")),
        Tool("story.scene.sync", "Marca una escena como reconciliada DESPUES de que la IA o el autor adaptaron los cambios entre gameplay y prosa. Guarda huellas de ambos lados para detectar deriva futura.", Schema(new
        {
            sceneId = Str("Id de la escena ya revisada en ambos formatos")
        }, "sceneId")),
        Tool("story.delete", "Borra un capitulo completo o una escena del Libro Espejo.", Schema(new
        {
            kind = EnumOf("Que borrar", "chapter", "scene"),
            id = Str("Id del capitulo o escena")
        }, "kind", "id")),
        Tool("story.query", "Lee el Libro Espejo, su conteo de palabras y el estado de sincronizacion. Con includeSources devuelve tambien las definiciones jugables enlazadas para adaptar juego a libro o libro a juego.", Schema(new
        {
            chapterId = Str("Filtrar por un capitulo (opcional)"),
            sceneId = Str("Filtrar por una escena global (opcional)"),
            includeSources = Boolean("true = incluir mapas/eventos/dialogos/combates enlazados completos")
        })),
        Tool("story.import", "Importa un manuscrito o guion YA ESCRITO al Libro Espejo: la puerta de entrada para hacer un juego a partir de un texto. SOLO DEPOSITA PROSA: crea capitulos y escenas en estado draft, sin links, sin mapas, sin dialogos y sin tocar nada existente (siempre agrega). Despues se lee escena por escena con story.query y se construye el juego con las herramientas de siempre. Formato: '# titulo' abre capitulo y '## titulo' abre escena; un texto plano sin encabezados entra como un capitulo separado por cortes de escena (***, ---).", Schema(new
        {
            source = Str("Ruta del archivo .md/.txt (absoluta, o relativa a la carpeta del proyecto). Alternativa a text"),
            text = Str("Manuscrito inline, para fragmentos cortos. Alternativa a source"),
            defaultTitle = Str("Titulo del capitulo cuando el documento no declara encabezados (opcional)"),
            dryRun = Boolean("true = parsear y devolver el informe SIN escribir nada: sirve para revisar el corte en capitulos/escenas antes de aceptarlo")
        })),
        Tool("story.export", "Exporta el manuscrito a Markdown y DOCX editorial (Times New Roman 12, doble espacio, margenes de una pulgada, capitulos en pagina nueva). Todo ocurre localmente.", Schema(new
        {
            baseName = Str("Nombre base de los archivos dentro de build/book (opcional)"),
            strict = Boolean("true = rechazar la exportacion si faltan datos, prosa o hay escenas desincronizadas")
        })),
        Tool("query.content_graph", "Resume el contenido del proyecto: mapas, dialogos, combates, sprites, sfx, vfx, temas y fuentes.", Schema(new { })),
        Tool("query.map", "Lee un mapa EN DETALLE: matriz de tiles de una region (default: el mapa entero), warps, y eventos con posicion, sprite y paginas con sus condiciones. Leer antes de escribir: con esto la IA no necesita abrir project.json. Nota: la 'pagina activa' depende de flags de RUNTIME que el MCP no tiene; se devuelven las condiciones por pagina para razonar la presencia.", Schema(new
        {
            mapId = Str("Mapa a leer"),
            x = Int("Columna inicial de la region (opcional, default 0)"),
            y = Int("Fila inicial de la region (opcional, default 0)"),
            w = Int("Ancho de la region en tiles (opcional, default hasta el borde)"),
            h = Int("Alto de la region en tiles (opcional, default hasta el borde)")
        }, "mapId")),
        Tool("query.entity", "Lee una definicion COMPLETA con la misma forma camelCase que project.json. kind 'project' devuelve info global; kind 'quality' devuelve plan, rutas, checkpoints y contratos.", Schema(new
        {
            kind = EnumOf("Tipo de entidad", "map", "event", "dialogue", "actor", "item", "enemy", "battle", "skill", "shop", "song", "sprite", "tileset", "uitheme", "sfx", "vfx", "font", "variable", "project", "quality"),
            id = Str("Id de la entidad (ignorado para kind=project/quality)")
        }, "kind")),
        Tool("playtest.run", "Corre un GUION de partida determinista con el juego oculto y devuelve el reporte JSON (cada paso con ok/fallo y detalle + estado final del mundo): la IA JUEGA su propio contenido y se corrige. Pasos: 'event event.id [pageIndex]' (prepara y ejecuta una pagina exacta), 'checkpoint id', 'move up,up,left' (camina; paso bloqueado se saltea), 'face up', 'interact', 'auto' (confirma dialogos, ceremonias y combates enteros a fuerza de Atacar), 'choose N' (opcion 0-based), 'confirm'/'cancel'/'up'/'down', 'wait 1.5', 'assert-flag flag.x true', 'assert-map map.id', 'assert-item item.id', 'assert-pos x,y', 'assert-money N|MIN..MAX', 'assert-party actor.a,actor.b', 'assert-level actor.id N|MIN..MAX', 'screenshot nombre.png' y 'dump'. Un game over o caer al titulo aborta el guion y lo reporta. Determinista: mismo guion, mismo resultado.", Schema(new
        {
            steps = Arr("Pasos del guion, en orden", Str("Paso, ej 'move up,up' / 'interact' / 'auto' / 'assert-flag flag.x true'")),
            @out = Str("Nombre del reporte JSON dentro de build/ (default playtest-report.json)")
        }, "steps")),
        Tool("playtest.screenshot", "Corre el juego con ventana oculta y captura el lienzo 256x224 a PNG: asi la IA VE lo que construyo. Opcionalmente dispara un evento (dialogo/combate/swirl) o muestra el titulo.", Schema(new
        {
            frames = Int("Frames a simular antes de capturar (default 30; mas frames = typewriter/transiciones mas avanzados)"),
            @event = Str("Id de evento a disparar al arrancar (opcional, ej 'event.alcalde' para ver su dialogo)"),
            eventPageIndex = Int("Pagina 0-based exacta del evento a disparar (opcional; permite capturar una variante aunque sus flags todavia no esten activas)"),
            title = Boolean("true = capturar la pantalla de titulo"),
            splash = Boolean("true = capturar la placa de arranque 'MADE WITH 90s ENGINE' (frames 100 = completa; frames 40 = coreografia de encendido a mitad)"),
            crt = Boolean("true = capturar con el filtro CRT aplicado (vidrio de tubo a 3x, como lo ve el jugador)"),
            editor = Boolean("true = capturar con el modo editor abierto (grilla, ids, paleta de tiles, minimapa)"),
            editorZoom = Int("Zoom out del editor en la captura: 1 (default), 2 (mapa a 1/2) o 4 (mapa a 1/4)"),
            editorTool = Int("Herramienta del editor en la captura: 0=TILES 1=OBJETOS 2=EVENTOS 3=WARPS 4=DIALOGO 5=LIBRO 6=FLAGS 7=LOG (default 0)"),
            pause = Boolean("true = capturar con el menu de pausa abierto"),
            pauseSection = Int("Seccion del menu de pausa a mostrar (0=ITEMS 1=ESTADO 2=EQUIPO 3=OPCIONES 4=GUARDAR 5=CARGAR; default 1)"),
            attack = Boolean("true = en el primer combate, atacar solo al primer blanco una vez (ver numero flotante de dano y log sin input)"),
            scrub = Str("Id de evento a abrir en el scrubber de cutscenes (opcional): captura el HUD con la cola de comandos del evento y el mundo congelado en ese punto"),
            scrubSteps = Int("Comandos a ejecutar en el scrubber antes de capturar (default 0 = pausado antes del primero; requiere scrub)"),
            scrubPageIndex = Int("Pagina 0-based exacta a cargar en el scrubber (opcional; sin esto usa la pagina activa por flags)"),
            map = Str("Capturar arrancando en este mapa (opcional; para ver mapas a los que se llega por warp)"),
            x = Int("Columna inicial del jugador si se indica map (default 1)"),
            y = Int("Fila inicial del jugador si se indica map (default 1)"),
            @out = Str("Nombre del PNG dentro de build/ (default screenshot.png)")
        }))
    ];

    public static ToolPayload Call(string name, JsonObject a, CommandSession s) => name switch
    {
        "project.set_info" => s.Mutate(p => { p.Id = GetStr(a, "id", p.Id); p.Title = GetStr(a, "title", p.Title); p.StartMapId = GetStr(a, "startMapId", p.StartMapId); p.StartEventId = GetStr(a, "startEventId", p.StartEventId); p.StartX = GetInt(a, "startX", p.StartX); p.StartY = GetInt(a, "startY", p.StartY); p.PlayerSpriteId = GetStr(a, "playerSpriteId", p.PlayerSpriteId); p.UiThemeId = GetStr(a, "uiThemeId", p.UiThemeId); p.Render.CrtFilter = GetBool(a, "crtFilter", p.Render.CrtFilter); p.Render.ShowDayClock = GetBool(a, "showDayClock", p.Render.ShowDayClock); p.Render.WarpTransition = GetStr(a, "warpTransition", p.Render.WarpTransition); p.Render.TitleImage = GetStr(a, "titleImage", p.Render.TitleImage); p.Render.TitleVfxId = GetStr(a, "titleVfxId", p.Render.TitleVfxId); p.Render.Language = GetStr(a, "language", p.Render.Language); p.StartMoney = GetInt(a, "startMoney", p.StartMoney); if (a["partyActorIds"] is not null) p.PartyActorIds = ReadStrings(a, "partyActorIds"); }),
        "project.design" => DesignDoc(a, s),
        "evals.run" => s.Read(_ => EvalSuite.Run(s.ProjectRoot, GetStr(a, "only"))),
        "content.delete" => s.Mutate(p =>
        {
            var kind = GetStr(a, "kind"); var id = GetStr(a, "id");
            var removed = kind switch
            {
                // Borrar un mapa arrastra sus eventos; borrar un evento sale de los eventIds.
                "map" => p.Events.RemoveAll(e => e.MapId == id) + p.Maps.RemoveAll(x => x.Id == id),
                "event" => p.Events.RemoveAll(x => x.Id == id) + p.Maps.Sum(m => m.EventIds.RemoveAll(e => e == id)),
                "dialogue" => p.Dialogues.RemoveAll(x => x.Id == id),
                "actor" => p.Actors.RemoveAll(x => x.Id == id) + p.PartyActorIds.RemoveAll(x => x == id),
                "item" => p.Items.RemoveAll(x => x.Id == id),
                "enemy" => p.Enemies.RemoveAll(x => x.Id == id),
                "battle" => p.Battles.RemoveAll(x => x.Id == id),
                "skill" => p.Skills.RemoveAll(x => x.Id == id),
                "shop" => p.Shops.RemoveAll(x => x.Id == id),
                "song" => p.Songs.RemoveAll(x => x.Id == id),
                "sprite" => p.Sprites.RemoveAll(x => x.Id == id),
                "tileset" => p.Tilesets.RemoveAll(x => x.Id == id),
                "uitheme" => p.UiThemes.RemoveAll(x => x.Id == id),
                "sfx" => p.Sfx.RemoveAll(x => x.Id == id),
                "vfx" => p.Vfx.RemoveAll(x => x.Id == id),
                "font" => p.Fonts.RemoveAll(x => x.Id == id),
                "variable" => p.Variables.RemoveAll(x => x.Id == id),
                _ => throw new InvalidOperationException($"Tipo desconocido '{kind}'."),
            };
            if (removed == 0) throw new InvalidOperationException($"No existe {kind} con id '{id}'.");
        }),
        "map.set_info" => s.Mutate(p => { var m = p.Maps.First(x => x.Id == GetStr(a, "mapId")); m.Name = GetStr(a, "name", m.Name); if (a["songId"] is not null) m.SongId = GetStr(a, "songId", m.SongId); if (a["weatherVfxId"] is not null) m.WeatherVfxId = GetStr(a, "weatherVfxId", m.WeatherVfxId); }),
        "project.validate" => s.Validate(),
        "project.audit" => s.Read(p => DesignAudit.Analyze(p, GetBool(a, "includeInfo", true))),
        "scene.audit" => SceneAuditTool(a, s),
        "balance.audit" => s.Read(p => BalanceAudit.Analyze(p, GetBool(a, "includeInfo", true), GetStr(a, "routeId"))),
        "quality.audit" => s.Read(p => QualityAudit.Analyze(
            p, s.ProjectRoot, GetBool(a, "includeInfo", true), GetBool(a, "runPlaytests"), GetStr(a, "routeId"))),
        "quality.plan.set" => s.Mutate(p =>
        {
            var plan = p.QualityPlan;
            plan.EnforceOnPack = GetBool(a, "enforceOnPack", plan.EnforceOnPack);
            plan.RunPlaytestsOnPack = GetBool(a, "runPlaytestsOnPack", plan.RunPlaytestsOnPack);
            plan.AuditAllScenes = GetBool(a, "auditAllScenes", plan.AuditAllScenes);
            if (a["canonicalRouteId"] is not null) plan.CanonicalRouteId = GetStr(a, "canonicalRouteId");
            if (a["encounters"] is not null) plan.Encounters = ReadList<QualityEncounterDef>(a, "encounters");
        }),
        "quality.route.set" => s.Mutate(p =>
        {
            var route = new QualityRouteDef
            {
                Id = GetStr(a, "id"),
                Name = GetStr(a, "name"),
                Description = GetStr(a, "description"),
                CanonChoices = ReadList<StoryCanonChoiceDef>(a, "canonChoices"),
                Checkpoints = ReadList<QualityCheckpointDef>(a, "checkpoints")
            };
            Upsert(p.QualityPlan.Routes, x => x.Id == route.Id, route);
        }),
        "quality.route.delete" => s.Mutate(p =>
        {
            var id = GetStr(a, "id");
            if (p.QualityPlan.Routes.RemoveAll(x => x.Id == id) == 0)
                throw new InvalidOperationException($"No existe la ruta '{id}'.");
            if (p.QualityPlan.CanonicalRouteId == id) p.QualityPlan.CanonicalRouteId = "";
        }),
        "project.build_pack" => s.Pack(),
        "project.asset_report" => s.Read(p => new { text = AssetPipeline.Validate(p, s.ProjectRoot).ToHumanText(), ok = AssetPipeline.Validate(p, s.ProjectRoot).Ok }),
        "project.export_assets" => s.Read(p => AssetPipeline.WriteManifest(p, GetStr(a, "projectRoot", s.ProjectRoot))),
        "transaction.undo" => s.Undo(),
        "transaction.redo" => s.Redo(),
        "batch.preview" => RunBatch(a, s, preview: true),
        "batch.apply" => RunBatch(a, s, preview: false),
        "variable.define" => s.Mutate(p => Upsert(p.Variables, x => x.Id == GetStr(a, "id"), new GameVariable { Id = GetStr(a, "id"), Kind = EnumVal(a, "kind", VariableKind.Flag), Default = GetStr(a, "default", "false") })),
        "tileset.create" => s.Mutate(p => Upsert(p.Tilesets, x => x.Id == GetStr(a, "id"), new TilesetDef { Id = GetStr(a, "id"), Image = GetStr(a, "image", "generated"), TileSize = GetInt(a, "tileSize", 16), AnimMs = GetInt(a, "animMs", 300), Tiles = ReadList<TileDef>(a, "tiles") })),
        "map.create" => s.Mutate(p => { var w = GetInt(a, "width", 16); var h = GetInt(a, "height", 16); Upsert(p.Maps, x => x.Id == GetStr(a, "id"), new MapDef { Id = GetStr(a, "id"), Name = GetStr(a, "name"), TilesetId = GetStr(a, "tilesetId"), Width = w, Height = h, Tiles = Enumerable.Repeat(GetInt(a, "fillTile", 0), w * h).ToList(), SongId = GetStr(a, "songId", "") }); }),
        "map.set_warps" => s.Mutate(p => { var m = p.Maps.First(x => x.Id == GetStr(a, "mapId")); m.Warps = ReadList<WarpDef>(a, "warps"); }),
        "map.paint_rect" => s.Mutate(p => MapOps.Fill(p.Maps.First(x => x.Id == GetStr(a, "mapId")), GetInt(a, "x"), GetInt(a, "y"), GetInt(a, "width", 1), GetInt(a, "height", 1), GetInt(a, "tileId"), GetInt(a, "flags", 0))),
        "map.paint_tiles" => s.Mutate(p =>
        {
            var cells = ReadCells(a);
            if (cells.Count == 0) throw new InvalidOperationException("cells vacio: indicar al menos una celda {x,y,tile}.");
            MapOps.PaintCells(p.Maps.First(x => x.Id == GetStr(a, "mapId")), cells, strict: true, GetInt(a, "flags", 0)); // celda fuera = error con la celda exacta
        }),
        "map.flood_fill" => FloodFillTool(a, s),
        "event.create" => s.Mutate(p => { var ev = new EventDef { Id = GetStr(a, "id"), MapId = GetStr(a, "mapId"), Name = GetStr(a, "name"), Kind = EnumVal(a, "kind", EventKind.Npc), X = GetInt(a, "x"), Y = GetInt(a, "y"), Sprite = GetStr(a, "sprite"), RoutineId = GetStr(a, "routineId", "idle"), Pages = [new EventPage()] }; Upsert(p.Events, x => x.Id == ev.Id, ev); var m = p.Maps.FirstOrDefault(x => x.Id == ev.MapId); if (m != null && !m.EventIds.Contains(ev.Id)) m.EventIds.Add(ev.Id); }),
        "event.set_commands" => s.Mutate(p => { var ev = p.Events.First(x => x.Id == GetStr(a, "eventId")); if (ev.Pages.Count == 0) ev.Pages.Add(new EventPage()); ev.Pages[0].Commands = ReadList<EventCommand>(a, "commands"); }),
        "event.set_pages" => s.Mutate(p => { var ev = p.Events.First(x => x.Id == GetStr(a, "eventId")); ev.Pages = ReadList<EventPage>(a, "pages"); if (ev.Pages.Count == 0) ev.Pages.Add(new EventPage()); }),
        "dialogue.create" => s.Mutate(p => Upsert(p.Dialogues, x => x.Id == GetStr(a, "id"), new DialogueDef { Id = GetStr(a, "id"), StartNodeId = GetStr(a, "startNodeId"), Nodes = ReadList<DialogueNode>(a, "nodes") })),
        "actor.create" => s.Mutate(p => Upsert(p.Actors, x => x.Id == GetStr(a, "id"), new ActorDef { Id = GetStr(a, "id"), Name = GetStr(a, "name"), Level = GetInt(a, "level", 1), Stats = ReadObj(a, "stats", new StatBlock()), Growth = a["growth"] is null ? null : ReadObj(a, "growth", new StatBlock()), SkillIds = ReadStrings(a, "skillIds") })),
        "skill.create" => s.Mutate(p => Upsert(p.Skills, x => x.Id == GetStr(a, "id"), new SkillDef { Id = GetStr(a, "id"), Name = GetStr(a, "name"), MpCost = GetInt(a, "mpCost", 1), Power = GetInt(a, "power", 5), Kind = GetStr(a, "kind", "damage"), Status = GetStr(a, "status", ""), VfxId = GetStr(a, "vfxId", "") })),
        "item.create" => s.Mutate(p => Upsert(p.Items, x => x.Id == GetStr(a, "id"), new ItemDef { Id = GetStr(a, "id"), Name = GetStr(a, "name"), Price = GetInt(a, "price"), Effect = GetStr(a, "effect"), Slot = GetStr(a, "slot", ""), Bonus = ReadBonus(a), Description = GetStr(a, "description", ""), SpriteId = GetStr(a, "spriteId", "") })),
        "enemy.create" => s.Mutate(p => Upsert(p.Enemies, x => x.Id == GetStr(a, "id"), new EnemyDef { Id = GetStr(a, "id"), Name = GetStr(a, "name"), Stats = ReadObj(a, "stats", new StatBlock()), Exp = GetInt(a, "exp"), Money = GetInt(a, "money"), Inflicts = GetStr(a, "inflicts", "") })),
        "battle.create" => s.Mutate(p => Upsert(p.Battles, x => x.Id == GetStr(a, "id"), new BattleDef { Id = GetStr(a, "id"), View = EnumVal(a, "view", BattleView.Frontal), RollingHp = GetBool(a, "rollingHp"), EnemyIds = ReadStrings(a, "enemyIds"), VictoryFlag = GetStr(a, "victoryFlag"), DamageFormula = GetStr(a, "damageFormula", "max(1, attack - defense)"), SongId = GetStr(a, "songId", ""), Boss = GetBool(a, "boss"), BackgroundVfxId = GetStr(a, "backgroundVfxId", "") })),
        "shop.create" => s.Mutate(p => Upsert(p.Shops, x => x.Id == GetStr(a, "id"), new ShopDef { Id = GetStr(a, "id"), Name = GetStr(a, "name"), ItemIds = ReadStrings(a, "itemIds") })),
        "song.create" => s.Mutate(p => Upsert(p.Songs, x => x.Id == GetStr(a, "id"), new SongDef { Id = GetStr(a, "id"), Tempo = GetInt(a, "tempo", 120), Channels = ReadList<SongChannel>(a, "channels") })),
        "sprite.create" => s.Mutate(p => Upsert(p.Sprites, x => x.Id == GetStr(a, "id"), new SpriteDef { Id = GetStr(a, "id"), Name = GetStr(a, "name"), Width = GetInt(a, "width", 16), Height = GetInt(a, "height", 16), Palette = ReadStrings(a, "palette"), Poses = ReadList<SpritePose>(a, "poses") })),
        "sprite.import_sheet" => s.Mutate(p => Upsert(p.Sprites, x => x.Id == GetStr(a, "id"), new SpriteDef { Id = GetStr(a, "id"), Name = GetStr(a, "name"), Width = GetInt(a, "frameWidth", 16), Height = GetInt(a, "frameHeight", 16), Image = GetStr(a, "image"), SheetFramesPerDirection = GetInt(a, "framesPerDirection", 2) })),
        "font.import" => s.Mutate(p => Upsert(p.Fonts, x => x.Id == GetStr(a, "id"), new FontDef { Id = GetStr(a, "id"), Image = GetStr(a, "image"), GlyphWidth = GetInt(a, "glyphWidth", 8), GlyphHeight = GetInt(a, "glyphHeight", 8), Charset = GetStr(a, "charset"), VariableWidth = GetBool(a, "variableWidth", true) })),
        "uitheme.set" => s.Mutate(p =>
        {
            var id = GetStr(a, "id");
            var current = p.UiThemes.FirstOrDefault(x => x.Id == id) ?? new UiThemeDef { Id = id };
            Upsert(p.UiThemes, x => x.Id == id, new UiThemeDef
            {
                Id = id,
                WindowBg = GetStr(a, "windowBg", current.WindowBg),
                WindowBorder = GetStr(a, "windowBorder", current.WindowBorder),
                TextColor = GetStr(a, "textColor", current.TextColor),
                AccentColor = GetStr(a, "accentColor", current.AccentColor),
                ShadowColor = GetStr(a, "shadowColor", current.ShadowColor),
                FontId = GetStr(a, "fontId", current.FontId),
                Style = GetStr(a, "style", current.Style),
                TextSpeedCps = GetInt(a, "textSpeedCps", current.TextSpeedCps)
            });
            if (GetBool(a, "makeActive", true)) p.UiThemeId = id;
        }),
        "sfx.create" => s.Mutate(p => Upsert(p.Sfx, x => x.Id == GetStr(a, "id"), new SfxDef { Id = GetStr(a, "id"), Wave = GetStr(a, "wave", "square"), StartFreq = GetNum(a, "startFreq", 440), EndFreq = GetNum(a, "endFreq", GetNum(a, "startFreq", 440)), DurationMs = GetInt(a, "durationMs", 120), Decay = GetNum(a, "decay", 0.6), Volume = GetNum(a, "volume", 0.5), Duty = GetNum(a, "duty", 0.5) })),
        "vfx.create" => s.Mutate(p => Upsert(p.Vfx, x => x.Id == GetStr(a, "id"), new VfxDef { Id = GetStr(a, "id"), Kind = GetStr(a, "kind", "impact"), DurationMs = GetInt(a, "durationMs", 600), SfxId = GetStr(a, "sfxId", ""), Layers = ReadList<VfxLayer>(a, "layers") })),
        "story.book.set" => StoryBookSet(a, s),
        "story.chapter.set" => StoryChapterSet(a, s),
        "story.scene.set" => StorySceneSet(a, s),
        "story.scene.sync" => StorySceneSync(a, s),
        "story.delete" => StoryDelete(a, s),
        "story.import" => StoryImport(a, s),
        "story.query" => StoryQuery(a, s),
        "story.export" => s.Read(p => StoryBookExporter.Export(p, s.ProjectRoot, GetStr(a, "baseName"), GetBool(a, "strict"))),
        "playtest.run" => s.Read(p =>
        {
            var validation = ProjectValidator.Validate(p);
            if (!validation.Ok) throw new InvalidOperationException("El proyecto no valida; corregir antes de jugar: " + validation.ToHumanText());
            var steps = ReadStrings(a, "steps");
            if (steps.Count == 0) throw new InvalidOperationException("steps vacio: pasar los pasos del guion (move/interact/auto/choose/assert-*...).");
            var runtime = new VisualRuntime(p, s.ProjectRoot);
            runtime.DebugRunScript(steps);
            runtime.Run(30000, null, hidden: true, crtCapture: GetBool(a, "crt"));
            var report = runtime.BuildScriptReport();
            var reportFile = Path.Combine(s.ProjectRoot, "build", GetStr(a, "out", "playtest-report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);
            File.WriteAllText(reportFile, report);
            return JsonNode.Parse(report) ?? new JsonObject();
        }),
        "playtest.screenshot" => s.Read(p =>
        {
            var validation = ProjectValidator.Validate(p);
            if (!validation.Ok) throw new InvalidOperationException("El proyecto no valida; corregir antes de capturar: " + validation.ToHumanText());
            var output = Path.Combine(s.ProjectRoot, "build", GetStr(a, "out", "screenshot.png"));
            var runtime = new VisualRuntime(p, s.ProjectRoot);
            var eventId = GetStr(a, "event");
            if (!string.IsNullOrWhiteSpace(eventId)) runtime.DebugStartEvent(eventId, GetInt(a, "eventPageIndex", -1));
            var startMap = GetStr(a, "map");
            if (!string.IsNullOrWhiteSpace(startMap)) runtime.DebugStartAt(startMap, GetInt(a, "x", 1), GetInt(a, "y", 1));
            if (GetBool(a, "title")) runtime.ForceTitle();
            if (GetBool(a, "splash")) runtime.ForceSplash();
            if (GetBool(a, "editor")) runtime.ForceEditor(GetInt(a, "editorZoom", 1), GetInt(a, "editorTool", 0));
            if (GetBool(a, "pause")) runtime.ForcePause(GetInt(a, "pauseSection", 1));
            if (GetBool(a, "attack")) runtime.DebugAutoAttack();
            var scrubEvent = GetStr(a, "scrub");
            if (!string.IsNullOrWhiteSpace(scrubEvent)) runtime.DebugScrub(scrubEvent, GetInt(a, "scrubSteps", 0), GetInt(a, "scrubPageIndex", -1));
            runtime.Run(GetInt(a, "frames", 30), output, hidden: true, crtCapture: GetBool(a, "crt"));
            return new { screenshot = output };
        }),
        "query.map" => QueryMap(a, s),
        "query.entity" => QueryEntity(a, s),
        "query.content_graph" => s.Read(p => new
        {
            p.Title,
            p.PlayerSpriteId,
            p.UiThemeId,
            maps = p.Maps.Select(x => new { x.Id, x.Name, events = x.EventIds }),
            dialogues = p.Dialogues.Select(x => new { x.Id, nodes = x.Nodes.Count }),
            battles = p.Battles.Select(x => new { x.Id, x.View, enemies = x.EnemyIds }),
            sprites = p.Sprites.Select(x => new { x.Id, x.Width, x.Height, source = string.IsNullOrWhiteSpace(x.Image) ? "procedural" : x.Image }),
            uiThemes = p.UiThemes.Select(x => x.Id),
            sfx = p.Sfx.Select(x => x.Id),
            vfx = p.Vfx.Select(x => new { x.Id, x.Kind }),
            fonts = p.Fonts.Select(x => x.Id),
            storyBook = new
            {
                p.StoryBook.Title,
                p.StoryBook.Author,
                chapters = p.StoryBook.Chapters.Count,
                scenes = p.StoryBook.Chapters.Sum(x => x.Scenes.Count),
                words = NarrativeTwin.WordCount(p.StoryBook)
            },
            qualityPlan = new
            {
                p.QualityPlan.EnforceOnPack,
                p.QualityPlan.RunPlaytestsOnPack,
                p.QualityPlan.AuditAllScenes,
                p.QualityPlan.CanonicalRouteId,
                routes = p.QualityPlan.Routes.Select(x => new { x.Id, x.Name, checkpoints = x.Checkpoints.Count }),
                encounters = p.QualityPlan.Encounters.Count
            }
        }),
        _ => ToolPayload.Fail("unknown_tool", $"Herramienta no registrada: {name}", "Usar tools/list.")
    };

    /// <summary>Entrada corta de la bitacora [ia] para una herramienta de ESCRITURA exitosa
    /// (nombre + ids principales, una linea). null = lectura/side-effect, no se anota.</summary>
    public static string? WriteNote(string name, JsonObject a) => name switch
    {
        _ when name.StartsWith("query.") => null,
        "project.validate" or "project.audit" or "scene.audit" or "balance.audit" or "quality.audit" or "project.build_pack" or "project.asset_report" or "project.export_assets"
            or "batch.preview"
            or "playtest.screenshot" or "playtest.run" or "story.query" or "story.export" or "evals.run" => null,
        "transaction.undo" => "undo",
        "transaction.redo" => "redo",
        // Leer el guion no es una operacion de autoria; reescribirlo si.
        "project.design" => a["text"] is null ? null : "project.design (reescribe design.md)",
        "project.set_info" => "project.set_info",
        "content.delete" => $"content.delete {GetStr(a, "kind")} {GetStr(a, "id")}",
        "map.paint_rect" => $"map.paint_rect {GetInt(a, "width", 1)}x{GetInt(a, "height", 1)} en {GetStr(a, "mapId")}",
        "map.paint_tiles" => $"map.paint_tiles {(a["cells"] as JsonArray)?.Count ?? 0} celdas en {GetStr(a, "mapId")}",
        "map.flood_fill" => $"map.flood_fill en {GetStr(a, "mapId")}",
        "map.set_info" or "map.set_warps" => $"{name} {GetStr(a, "mapId")}",
        "event.set_commands" or "event.set_pages" => $"{name} {GetStr(a, "eventId")}",
        "story.scene.sync" => $"story.scene.sync {GetStr(a, "sceneId")}",
        // Una vista previa no escribe: no ensucia la bitacora de co-autoria.
        "story.import" => GetBool(a, "dryRun") ? null : $"story.import {(string.IsNullOrWhiteSpace(GetStr(a, "source")) ? "texto inline" : GetStr(a, "source"))}",
        "quality.plan.set" => "quality.plan.set",
        "quality.route.set" or "quality.route.delete" => $"{name} {GetStr(a, "id")}",
        "batch.apply" => $"batch.apply {(a["calls"] as JsonArray)?.Count ?? 0} operaciones",
        _ => $"{name} {GetStr(a, "id")}".TrimEnd(),
    };

    /// <summary>Herramientas que pueden ir dentro de batch.apply: solo escrituras Mutate-based.
    /// Quedan afuera las lecturas, las capturas (side-effect), undo/redo y el batch anidado.</summary>
    static readonly HashSet<string> Batchable =
    [
        "project.set_info", "content.delete", "map.set_info", "variable.define", "tileset.create",
        "map.create", "map.paint_rect", "map.paint_tiles", "map.flood_fill", "map.set_warps",
        "event.create", "event.set_commands", "event.set_pages", "dialogue.create", "actor.create",
        "skill.create", "item.create", "enemy.create", "battle.create", "shop.create", "song.create",
        "sprite.create", "sprite.import_sheet", "font.import", "uitheme.set", "sfx.create", "vfx.create",
        "story.book.set", "story.chapter.set", "story.scene.set", "story.scene.sync", "story.delete", "story.import",
        "quality.plan.set", "quality.route.set", "quality.route.delete",
    ];

    static ToolPayload SceneAuditTool(JsonObject a, CommandSession s)
    {
        var eventId = GetStr(a, "eventId");
        var storySceneId = GetStr(a, "storySceneId");
        if (string.IsNullOrWhiteSpace(eventId) == string.IsNullOrWhiteSpace(storySceneId))
            return ToolPayload.Fail("bad_scene_scope",
                "Indicar exactamente uno: eventId o storySceneId.",
                "Usar eventId para una cutscene/evento jugable; storySceneId para auditar el conjunto enlazado del Libro Espejo.");
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            var ev = s.Project.Events.FirstOrDefault(x => x.Id == eventId);
            if (ev == null)
                return ToolPayload.Fail("missing_scene_event", $"No existe el evento '{eventId}'.", "Consultar query.map/query.entity para listar eventos.");
            var pageId = GetStr(a, "pageId");
            var hasPageIndex = a["pageIndex"] is not null;
            var pageIndex = hasPageIndex ? GetInt(a, "pageIndex") : -1;
            if (!string.IsNullOrWhiteSpace(pageId) && hasPageIndex)
                return ToolPayload.Fail("ambiguous_page_selector", "Usar pageId o pageIndex, no ambos.", "Quitar uno de los dos selectores.");
            if (hasPageIndex && (pageIndex < 0 || pageIndex >= ev.Pages.Count))
                return ToolPayload.Fail("missing_scene_page", $"El evento '{eventId}' no tiene pagina en indice {pageIndex}.", $"Usar un indice entre 0 y {Math.Max(0, ev.Pages.Count - 1)}.");
            var matchingPages = string.IsNullOrWhiteSpace(pageId) ? 0 : ev.Pages.Count(x => x.Id == pageId);
            if (!string.IsNullOrWhiteSpace(pageId) && matchingPages == 0)
                return ToolPayload.Fail("missing_scene_page", $"El evento '{eventId}' no tiene una pagina '{pageId}'.", "Omitir pageId para auditar todas, o usar el id real de event.pages.");
            if (matchingPages > 1)
                return ToolPayload.Fail("ambiguous_scene_page", $"El evento '{eventId}' tiene {matchingPages} paginas llamadas '{pageId}'.", "Usar pageIndex para elegir una pagina exacta.");
            return s.Read(p => SceneAudit.AnalyzeEvent(p, eventId, pageId,
                GetBool(a, "includeInfo", true), GetBool(a, "includeTranscript", true), pageIndex));
        }
        if (NarrativeTwin.FindScene(s.Project, storySceneId) == null)
            return ToolPayload.Fail("missing_story_scene", $"No existe la escena '{storySceneId}'.", "Consultar story.query para listar las escenas.");
        if (a["pageId"] is not null || a["pageIndex"] is not null)
            return ToolPayload.Fail("page_without_event", "pageId/pageIndex solo se pueden usar junto con eventId.", "Quitar el selector de pagina al auditar una escena del Libro Espejo.");
        return s.Read(p => SceneAudit.AnalyzeStoryScene(p, storySceneId,
            GetBool(a, "includeInfo", true), GetBool(a, "includeTranscript", true)));
    }

    /// <summary>Motor compartido de batch.apply/preview: misma whitelist, orden y validacion.
    /// Preview completa contra un clon; apply persiste una vez y crea un solo undo.</summary>
    static ToolPayload RunBatch(JsonObject a, CommandSession s, bool preview)
    {
        if (a["calls"] is not JsonArray calls || calls.Count == 0)
            return ToolPayload.Fail("empty_batch", "calls vacio: indicar al menos una llamada {name, arguments}.", "Ver el inputSchema de batch.apply.");
        if (a["expectedRevision"] is not null)
        {
            var expected = GetInt(a, "expectedRevision");
            var current = s.DiskRevision;
            if (expected != current)
                return ToolPayload.Fail("stale_preview", $"La propuesta fue revisada sobre revision {expected}, pero el disco esta en {current}.", "Ejecutar batch.preview otra vez sobre la revision actual antes de aplicar.");
        }
        var begin = preview ? s.BeginPreviewBatch() : s.BeginBatch();
        if (!begin.Ok) return begin;
        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i] as JsonObject;
            var name = call?["name"]?.GetValue<string>() ?? "";
            if (call == null || !Batchable.Contains(name))
            {
                s.AbortBatch();
                return ToolPayload.Fail("not_batchable", $"Paso {i} ('{name}'): esa herramienta no puede ir en un batch.", "Solo escrituras (create/set/delete/paint/fill/define). Lecturas, capturas, undo/redo y batch.preview/apply anidados van como llamadas sueltas.");
            }
            ToolPayload step;
            try { step = Call(name, call["arguments"] as JsonObject ?? [], s); }
            catch (Exception ex) { step = ToolPayload.Fail("mutation_failed", ex.Message, "Revisar los argumentos del paso."); }
            if (!step.Ok)
            {
                s.AbortBatch();
                return ToolPayload.Fail("batch_step_failed", $"Paso {i} ({name}): {step.Error!.Message}", $"{step.Error.Fix} El batch se revirtio ENTERO: ningun cambio quedo aplicado.");
            }
        }
        if (preview) return s.CompletePreviewBatch(calls.Count);
        var commit = s.CommitBatch();
        return commit.Ok ? ToolPayload.Success(new { applied = calls.Count, revision = s.Project.Revision }) : commit;
    }

    /// <summary>Las celdas de map.paint_tiles: lista de {x,y,tile}.</summary>
    static List<(int X, int Y, int Tile)> ReadCells(JsonObject a) =>
        a["cells"] is not JsonArray arr ? [] : [.. arr.OfType<JsonObject>().Select(c => (GetInt(c, "x"), GetInt(c, "y"), GetInt(c, "tile")))];

    /// <summary>map.flood_fill: calcula las celdas sobre el proyecto actual; 0 celdas = exito
    /// sin transaccion (el origen ya era ese tile), si no pinta todas en UNA transaccion.</summary>
    static ToolPayload FloodFillTool(JsonObject a, CommandSession s)
    {
        var mapId = GetStr(a, "mapId");
        var m = s.Project.Maps.FirstOrDefault(x => x.Id == mapId);
        if (m == null) return ToolPayload.Fail("missing_map", $"No existe el mapa '{mapId}'.", "query.content_graph lista los mapas del proyecto.");
        var tile = GetInt(a, "tileId");
        var cells = MapOps.FloodFill(m, GetInt(a, "x"), GetInt(a, "y"), tile);
        if (cells.Count == 0) return ToolPayload.Success(new { painted = 0, note = "El origen ya era ese tile (o cae fuera del mapa): nada que rellenar." });
        var r = s.Mutate(p => MapOps.PaintCells(p.Maps.First(x => x.Id == mapId), cells.Select(c => (c.X, c.Y, tile)), strict: false, GetInt(a, "flags", 0)));
        return r.Ok ? ToolPayload.Success(new { painted = cells.Count }) : r;
    }

    static ToolPayload StoryBookSet(JsonObject a, CommandSession s) => s.Mutate(p =>
    {
        var book = p.StoryBook;
        book.Title = GetStr(a, "title", book.Title);
        book.Subtitle = GetStr(a, "subtitle", book.Subtitle);
        book.Author = GetStr(a, "author", book.Author);
        book.ShortTitle = GetStr(a, "shortTitle", book.ShortTitle);
        book.Language = GetStr(a, "language", book.Language);
        book.Contact = GetStr(a, "contact", book.Contact);
        book.PageSize = GetStr(a, "pageSize", book.PageSize);
        book.Description = GetStr(a, "description", book.Description);
    });

    static ToolPayload StoryChapterSet(JsonObject a, CommandSession s)
    {
        var id = GetStr(a, "id");
        return s.Mutate(p =>
        {
            var current = p.StoryBook.Chapters.FirstOrDefault(x => x.Id == id);
            Upsert(p.StoryBook.Chapters, x => x.Id == id, new StoryChapterDef
            {
                Id = id,
                Title = GetStr(a, "title", current?.Title ?? ""),
                Summary = GetStr(a, "summary", current?.Summary ?? ""),
                Scenes = current?.Scenes ?? []
            });
        });
    }

    static ToolPayload StorySceneSet(JsonObject a, CommandSession s)
    {
        var chapterId = GetStr(a, "chapterId");
        var id = GetStr(a, "id");
        var chapter = s.Project.StoryBook.Chapters.FirstOrDefault(x => x.Id == chapterId);
        if (chapter == null)
            return ToolPayload.Fail("missing_story_chapter", $"No existe el capitulo '{chapterId}'.", "Crearlo primero con story.chapter.set.");
        var elsewhere = NarrativeTwin.Scenes(s.Project).FirstOrDefault(x => x.Scene.Id == id);
        if (elsewhere.Scene != null && elsewhere.Chapter.Id != chapterId)
            return ToolPayload.Fail("story_scene_in_other_chapter", $"La escena '{id}' ya pertenece a '{elsewhere.Chapter.Id}'.", "Borrarla o usar su chapterId actual; los ids de escena son globales.");
        return s.Mutate(p =>
        {
            var target = p.StoryBook.Chapters.First(x => x.Id == chapterId);
            var current = target.Scenes.FirstOrDefault(x => x.Id == id);
            Upsert(target.Scenes, x => x.Id == id, new StorySceneDef
            {
                Id = id,
                Title = GetStr(a, "title", current?.Title ?? ""),
                Synopsis = GetStr(a, "synopsis", current?.Synopsis ?? ""),
                Pov = GetStr(a, "pov", current?.Pov ?? ""),
                Location = GetStr(a, "location", current?.Location ?? ""),
                Time = GetStr(a, "time", current?.Time ?? ""),
                Status = GetStr(a, "status", current?.Status ?? "draft"),
                Prose = GetStr(a, "prose", current?.Prose ?? ""),
                Tags = a["tags"] is null ? current?.Tags.ToList() ?? [] : ReadStrings(a, "tags"),
                Links = a["links"] is null ? current?.Links.ToList() ?? [] : ReadList<StoryLinkDef>(a, "links"),
                CanonChoices = a["canonChoices"] is null ? current?.CanonChoices.ToList() ?? [] : ReadList<StoryCanonChoiceDef>(a, "canonChoices"),
                SyncedGameHash = current?.SyncedGameHash ?? "",
                SyncedProseHash = current?.SyncedProseHash ?? ""
            });
        });
    }

    static ToolPayload StorySceneSync(JsonObject a, CommandSession s)
    {
        var id = GetStr(a, "sceneId");
        if (NarrativeTwin.FindScene(s.Project, id) == null)
            return ToolPayload.Fail("missing_story_scene", $"No existe la escena '{id}'.", "Consultar story.query para listar las escenas.");
        return s.Mutate(p => NarrativeTwin.Sync(p, NarrativeTwin.FindScene(p, id)!));
    }

    static ToolPayload StoryDelete(JsonObject a, CommandSession s)
    {
        var kind = GetStr(a, "kind");
        var id = GetStr(a, "id");
        var exists = kind == "chapter"
            ? s.Project.StoryBook.Chapters.Any(x => x.Id == id)
            : kind == "scene" && NarrativeTwin.FindScene(s.Project, id) != null;
        if (!exists)
            return ToolPayload.Fail("missing_story_content", $"No existe {kind} '{id}'.", "Consultar story.query para listar el manuscrito.");
        return s.Mutate(p =>
        {
            if (kind == "chapter") p.StoryBook.Chapters.RemoveAll(x => x.Id == id);
            else foreach (var chapter in p.StoryBook.Chapters) chapter.Scenes.RemoveAll(x => x.Id == id);
        });
    }

    /// <summary>La rampa de entrada: un texto ya escrito entra como capitulos y escenas. Deliberadamente
    /// tonta respecto del gameplay (no infiere mapas ni dialogos) y aditiva (jamas pisa lo existente).</summary>
    static ToolPayload StoryImport(JsonObject a, CommandSession s)
    {
        var source = GetStr(a, "source");
        var inline = GetStr(a, "text");
        if (string.IsNullOrWhiteSpace(source) == string.IsNullOrWhiteSpace(inline))
            return ToolPayload.Fail("bad_story_source",
                "Indicar exactamente uno: source (ruta de archivo) o text (manuscrito inline).",
                "Usar source para un manuscrito completo; text para un fragmento corto.");

        string text;
        string defaultTitle;
        if (!string.IsNullOrWhiteSpace(source))
        {
            var path = Path.IsPathRooted(source) ? source : Path.Combine(s.ProjectRoot, source);
            if (!File.Exists(path))
                return ToolPayload.Fail("missing_story_file", $"No existe el archivo '{path}'.", "Pasar una ruta absoluta o relativa a la carpeta del proyecto.");
            if (new FileInfo(path).Length > StoryImporter.MaxCharacters)
                return ToolPayload.Fail("story_file_too_big", $"El archivo supera los {StoryImporter.MaxCharacters / 1_000_000} MB.", "Importar el manuscrito por partes (un archivo por capitulo o acto).");
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { return ToolPayload.Fail("story_read_failed", $"No se pudo leer '{path}': {ex.Message}", "Cerrar el archivo en otro programa y reintentar."); }
            defaultTitle = GetStr(a, "defaultTitle", Path.GetFileNameWithoutExtension(path));
        }
        else
        {
            text = inline;
            defaultTitle = GetStr(a, "defaultTitle", "Manuscrito");
        }

        var dryRun = GetBool(a, "dryRun");
        // El corte se recalcula DENTRO de la transaccion: si Mutate rebasea sobre un disco mas
        // fresco (otro co-autor escribio), los ids se deduplican contra ese proyecto, no contra
        // la copia con la que entramos.
        StoryImportReport Build(GameProject p) =>
            StoryImporter.Parse(text, defaultTitle, StoryImporter.ChapterIds(p), StoryImporter.SceneIds(p), dryRun);

        var preview = Build(s.Project);
        if (preview.SceneCount == 0)
            return ToolPayload.Fail("empty_story_source", "El texto no produjo ninguna escena.", "Revisar que el archivo tenga contenido (o que los encabezados tengan texto debajo).");
        if (dryRun) return ToolPayload.Success(preview.Summarize());

        StoryImportReport? applied = null;
        var result = s.Mutate(p => { applied = Build(p); StoryImporter.Apply(p, applied); });
        return result.Ok ? ToolPayload.Success(applied!.Summarize()) : result;
    }

    static ToolPayload StoryQuery(JsonObject a, CommandSession s)
    {
        var p = s.Project;
        var chapterId = GetStr(a, "chapterId");
        var sceneId = GetStr(a, "sceneId");
        var includeSources = GetBool(a, "includeSources");
        var chapters = p.StoryBook.Chapters
            .Where(ch => string.IsNullOrWhiteSpace(chapterId) || ch.Id == chapterId)
            .Where(ch => string.IsNullOrWhiteSpace(sceneId) || ch.Scenes.Any(scene => scene.Id == sceneId))
            .ToList();
        if (!string.IsNullOrWhiteSpace(chapterId) && chapters.Count == 0)
            return ToolPayload.Fail("missing_story_chapter", $"No existe el capitulo '{chapterId}' o no contiene la escena pedida.", "Consultar story.query sin filtros.");
        if (!string.IsNullOrWhiteSpace(sceneId) && !chapters.SelectMany(x => x.Scenes).Any(x => x.Id == sceneId))
            return ToolPayload.Fail("missing_story_scene", $"No existe la escena '{sceneId}'.", "Consultar story.query sin filtros.");
        return ToolPayload.Success(new
        {
            book = new
            {
                p.StoryBook.Title,
                p.StoryBook.Subtitle,
                p.StoryBook.Author,
                p.StoryBook.ShortTitle,
                p.StoryBook.Language,
                p.StoryBook.Contact,
                p.StoryBook.PageSize,
                p.StoryBook.Description
            },
            totals = new
            {
                chapters = p.StoryBook.Chapters.Count,
                scenes = p.StoryBook.Chapters.Sum(x => x.Scenes.Count),
                words = NarrativeTwin.WordCount(p.StoryBook),
                exportWarnings = NarrativeTwin.ExportWarnings(p)
            },
            chapters = chapters.Select(ch => new
            {
                ch.Id,
                ch.Title,
                ch.Summary,
                words = ch.Scenes.Sum(scene => NarrativeTwin.WordCount(scene.Prose)),
                scenes = ch.Scenes.Where(scene => string.IsNullOrWhiteSpace(sceneId) || scene.Id == sceneId).Select(scene => new
                {
                    scene.Id,
                    scene.Title,
                    scene.Synopsis,
                    scene.Pov,
                    scene.Location,
                    scene.Time,
                    scene.Status,
                    scene.Prose,
                    scene.Tags,
                    scene.Links,
                    scene.CanonChoices,
                    words = NarrativeTwin.WordCount(scene.Prose),
                    sync = NarrativeTwin.State(p, scene),
                    sources = includeSources ? NarrativeTwin.Sources(p, scene) : null
                })
            })
        });
    }

    /// <summary>query.map: lectura granular de un mapa (region de tiles + warps + eventos con paginas).</summary>
    static ToolPayload QueryMap(JsonObject a, CommandSession s)
    {
        var p = s.Project;
        var m = p.Maps.FirstOrDefault(x => x.Id == GetStr(a, "mapId"));
        if (m == null) return ToolPayload.Fail("missing_map", $"No existe el mapa '{GetStr(a, "mapId")}'.", "query.content_graph lista los mapas del proyecto.");
        var x0 = Math.Clamp(GetInt(a, "x", 0), 0, Math.Max(0, m.Width - 1));
        var y0 = Math.Clamp(GetInt(a, "y", 0), 0, Math.Max(0, m.Height - 1));
        var w = Math.Clamp(GetInt(a, "w", m.Width - x0), 1, m.Width - x0);
        var h = Math.Clamp(GetInt(a, "h", m.Height - y0), 1, m.Height - y0);
        var tiles = new int[h][];
        for (var yy = 0; yy < h; yy++)
        {
            tiles[yy] = new int[w];
            for (var xx = 0; xx < w; xx++) tiles[yy][xx] = m.Tiles[(y0 + yy) * m.Width + x0 + xx];
        }
        return ToolPayload.Success(new
        {
            id = m.Id,
            name = m.Name,
            width = m.Width,
            height = m.Height,
            tilesetId = m.TilesetId,
            songId = m.SongId,
            region = new { x = x0, y = y0, w, h },
            tiles, // matriz [fila][columna] de ids de tile de la region
            warps = m.Warps.Select(wp => new { wp.X, wp.Y, wp.ToMapId, wp.ToX, wp.ToY, wp.Transition }),
            events = p.Events.Where(e => e.MapId == m.Id).Select(e => new
            {
                e.Id,
                e.Name,
                kind = e.Kind.ToString(),
                e.X,
                e.Y,
                sprite = e.Sprite,
                e.Solid,
                e.RoutineId,
                pages = e.Pages.Select(pg => new { pg.Id, conditions = pg.Conditions.Select(c => new { c.VariableId, c.EqualsValue }), commands = pg.Commands.Count })
            })
        });
    }

    /// <summary>query.entity: el GET generico simetrico a content.delete. Devuelve la def completa
    /// con la MISMA forma camelCase que project.json (la IA razona sobre lo que ya conoce).</summary>
    static ToolPayload QueryEntity(JsonObject a, CommandSession s)
    {
        var p = s.Project;
        var kind = GetStr(a, "kind");
        var id = GetStr(a, "id");
        object? def = kind switch
        {
            // kind=project: la info global sin las listas de contenido (y sin embeddedFiles).
            "project" => new { p.Id, p.Title, p.StartMapId, p.StartEventId, p.StartX, p.StartY, p.PlayerSpriteId, p.UiThemeId, p.StartMoney, p.PartyActorIds, p.Render, p.Variables },
            "quality" => p.QualityPlan,
            "map" => p.Maps.FirstOrDefault(x => x.Id == id),
            "event" => p.Events.FirstOrDefault(x => x.Id == id),
            "dialogue" => p.Dialogues.FirstOrDefault(x => x.Id == id),
            "actor" => p.Actors.FirstOrDefault(x => x.Id == id),
            "item" => p.Items.FirstOrDefault(x => x.Id == id),
            "enemy" => p.Enemies.FirstOrDefault(x => x.Id == id),
            "battle" => p.Battles.FirstOrDefault(x => x.Id == id),
            "skill" => p.Skills.FirstOrDefault(x => x.Id == id),
            "shop" => p.Shops.FirstOrDefault(x => x.Id == id),
            "song" => p.Songs.FirstOrDefault(x => x.Id == id),
            "sprite" => p.Sprites.FirstOrDefault(x => x.Id == id),
            "tileset" => p.Tilesets.FirstOrDefault(x => x.Id == id),
            "uitheme" => p.UiThemes.FirstOrDefault(x => x.Id == id),
            "sfx" => p.Sfx.FirstOrDefault(x => x.Id == id),
            "vfx" => p.Vfx.FirstOrDefault(x => x.Id == id),
            "font" => p.Fonts.FirstOrDefault(x => x.Id == id),
            "variable" => p.Variables.FirstOrDefault(x => x.Id == id),
            _ => null,
        };
        if (kind is not ("project" or "quality") && !ContentKinds.Contains(kind)) return ToolPayload.Fail("unknown_kind", $"Tipo desconocido '{kind}'.", "Usar uno de los kinds del schema.");
        return def == null
            ? ToolPayload.Fail("not_found", $"No existe {kind} con id '{id}'.", "query.content_graph lista los ids existentes.")
            : ToolPayload.Success(def);
    }

    static readonly HashSet<string> ContentKinds = ["map", "event", "dialogue", "actor", "item", "enemy", "battle", "skill", "shop", "song", "sprite", "tileset", "uitheme", "sfx", "vfx", "font", "variable"];

    static object Tool(string name, string description, object inputSchema) => new { name, description, inputSchema };
    /// <summary>Lee o reescribe design.md. Deliberadamente FUERA del modelo de contenido: es el
    /// guion de produccion, no datos que el motor interprete, asi que no valida referencias ni
    /// entra en el undo (decirlo en la descripcion de la herramienta, no esconderlo). Lo que si
    /// hereda del store es la escritura atomica con backup: el documento de diseno de un capitulo
    /// entero no se pierde por un corte a mitad de escritura.</summary>
    static ToolPayload DesignDoc(JsonObject a, CommandSession s)
    {
        var path = Path.Combine(s.ProjectRoot, "design.md");
        if (a["text"] is null)
        {
            if (!File.Exists(path))
                return ToolPayload.Fail("missing_design_doc", "El proyecto no tiene design.md.",
                    "Crearlo con project.design pasando 'text' (premisa, tono, core loop, personajes, mapa de capitulos, biblia de estilo y convencion de flags).");
            return ToolPayload.Success(new { path, text = File.ReadAllText(path) });
        }

        var text = GetStr(a, "text");
        if (string.IsNullOrWhiteSpace(text))
            return ToolPayload.Fail("empty_design_doc", "El documento de diseno no puede quedar vacio.",
                "Omitir 'text' para leer, o mandar el documento completo para reemplazarlo.");
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            return ToolPayload.Fail("design_write_failed", $"No se pudo escribir design.md: {ex.Message}",
                "Reintentar (el archivo puede estar abierto en un editor).");
        }
        return ToolPayload.Success(new { path, words = text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length });
    }

    static string GetStr(JsonObject a, string k, string f = "") => a[k]?.GetValue<string>() ?? f;
    static int GetInt(JsonObject a, string k, int f = 0) => a[k]?.GetValue<int>() ?? f;
    static double GetNum(JsonObject a, string k, double f = 0) => a[k]?.GetValue<double>() ?? f;
    static bool GetBool(JsonObject a, string k, bool f = false) => a[k]?.GetValue<bool>() ?? f;
    static T EnumVal<T>(JsonObject a, string k, T f) where T : struct { var v = GetStr(a, k, ""); return Enum.TryParse<T>(v, true, out var p) ? p : f; }
    static List<T> ReadList<T>(JsonObject a, string k) => a[k] is null ? [] : a[k]!.Deserialize<List<T>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }) ?? [];
    static T ReadObj<T>(JsonObject a, string k, T f) => a[k] is null ? f : a[k]!.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }) ?? f;
    /// <summary>Bonus de equipo: los campos ausentes son 0 (los defaults de StatBlock son para actores, no para items).</summary>
    static StatBlock? ReadBonus(JsonObject a) => a["bonus"] is not JsonObject b ? null : new StatBlock
    { Hp = GetInt(b, "hp", 0), Mp = GetInt(b, "mp", 0), Attack = GetInt(b, "attack", 0), Defense = GetInt(b, "defense", 0), Speed = GetInt(b, "speed", 0) };
    static List<string> ReadStrings(JsonObject a, string k) => a[k] is null ? [] : a[k]!.Deserialize<List<string>>() ?? [];
    static void Upsert<T>(List<T> list, Func<T, bool> match, T value) { var i = list.FindIndex(x => match(x)); if (i >= 0) list[i] = value; else list.Add(value); }
}
