# tormenta-vtt

Virtual tabletop desktop para Tormenta20.

## Objetivo

Construir um VTT local leve e rápido baseado em Godot 4 + C#, com foco em uso pessoal, mapas, tokens, chat e automação inicial de Tormenta20.

## Estrutura do projeto

- `project.godot` — arquivo do projeto Godot 4.
- `tormenta-vtt.csproj` — projeto C# para scripts Godot.
- `Scenes/` — cenas do jogo e da interface.
- `Scripts/` — lógica de domínio, UI, serviços, importadores e regras.
- `Scripts/Importers/` — pontos de entrada para importação de JSON/PDF.
- `Scripts/Parsers/` — parser de documentos e dados.
- `Data/` — dados de exemplo e persistência local.
- `Content/` — base de conteúdo Tormenta20 (classes, condições, magias).
- `Assets/` — futuros recursos gráficos.

## Como abrir

1. Instale Godot 4 com suporte a Mono.
2. Abra `project.godot` no editor Godot.
3. Execute a cena principal.

## MVP implementado

- interface básica de mapa, chat e campanha.
- importação de mapa por imagem.
- token simples com estatísticas e movimento por arraste.
- chat com comando `/roll` e macros básicos.
- persistência local de campanha em JSON.

## Próximos passos

- adicionar edição completa de fichas Tormenta20.
- expandir combate automático e tracker de iniciativa.
- implementar multiplayer host/join.
- adicionar importador de conteúdo JSON/PDF.
