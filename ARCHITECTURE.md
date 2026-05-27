# Arquitetura do Tormenta VTT

## Visão geral

O projeto segue um modelo de aplicação local, simples e modular. A base usa Godot 4 + C# para UI integrada, 2D rápido e build `.exe` nativo no futuro.

## Camadas

- `Scenes/` — visual e layout de UI.
- `Scripts/Models/` — modelo de domínio para campanhas, tokens, ficha e chat.
- `Scripts/Services/` — persistência, parser de dados, regras e suporte a rede.
- `Scripts/UI/` — controladores de interface e painel de chat.
- `Scripts/Content/`, `Scripts/Importers/`, `Scripts/Parsers/` — módulos preparados para ingestão futura de conteúdo PDF/JSON.
- `Data/` — arquivos de campanha e conteúdo serializado.

## Fluxo de dados

1. ação do usuário na UI.
2. controlador de cena (`Main.cs`) notifica serviços.
3. serviços atualizam o modelo de domínio.
4. UI é atualizada a partir do modelo.
5. persistência grava/recupera JSON local.

## Persistência

- campanha salva/abre via JSON.
- modelo de domínio converte para `Godot.Collections.Dictionary`.
- `PersistenceService` é responsável por I/O local.

## Event system

- controllers expõem eventos para seleção de tokens e mensagens de chat.
- classes de serviço notificam o UI por callbacks e métodos públicos.
- arquitetura preparada para adicionar sinalização C# e eventos em Godot no futuro.

## Networking

- `Scripts/Network/NetworkService.cs` contém esqueleto de arquitetura de host/join.
- sincronização do mapa, token e chat será implementada como camada simples por estado.

## Conteúdo Tormenta20

- diretórios `Scripts/Content`, `Scripts/Importers` e `Scripts/Parsers` são base para futuras importações de PDFs e JSON.
- regras do sistema devem ser data-driven e não hardcoded.
- o projeto atual já separa domínios de campanha, mapa, token e chat.
