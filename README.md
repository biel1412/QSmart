<p align="center">
  <img src="assets/brand/qsmart-logo-horizontal.svg" alt="QSmart by Insi" width="320">
</p>

<p align="center"><b>Plataforma de qualidade da Insi</b> — decisões, inspeções, pareceres e operação em um só lugar.<br>
Feita em C# (ASP.NET Core Blazor WebAssembly) e publicada no GitHub Pages.</p>

<p align="center"><a href="https://biel1412.github.io/QSmart/"><b>Abrir o QSmart →</b></a></p>

---

## O que é

O QSmart é a evolução do painel **Qualidade Geral** (PySide6/Python) para a web, com as **mesmas funcionalidades**:
os mesmos cálculos, as mesmas regras de leitura de planilhas, os mesmos relatórios e as mesmas exportações.
Mudaram a tecnologia (C#), o lugar onde roda (navegador), o menu e a identidade visual.

As planilhas são lidas **no próprio computador do usuário**, dentro do navegador: nenhum arquivo é enviado para servidor.

## Módulos

| Grupo | Aba | O que faz |
|---|---|---|
| Decisões | **Resumo** | Aprovações x reprovações, motivos, devolutivas por segmento, filtros (usuários, segmentos, dias), classificação de procedimentos e relatório PDF |
| | **Usuários** | Ranking de devolutivas com busca e ordenação |
| Inspeção | **Evolução** | Desvios Interno / Externo / Chamados por autorização e por inspetor, por cooperativa e por colaborador, relatório PDF e gravação no histórico |
| | **Inspeção** | Inspeções do tipo Interno por inspetor e por dia, matriz inspetor × dia e exportação Excel |
| | **Histórico** | Meses gravados, filtros (origem, autorização, inspetor, erro), evolução mês a mês e mensagem para o Teams |
| Conformidade | **Pareceres** | Confere as justificativas com os pareceres padrão (documento .docx ou .pdf), sugere o padrão mais próximo e exporta o resultado |
| Operação | **Volume** | Cruza compilados com uma lista de usuários (planilha ou colada) |
| | **Monitor** | Ociosidade por colaborador e por dia a partir das finalizações |
| | **Corredor** | Une compilados (com remoção de repetidas e divisão em abas) e junta PDFs |
| Ferramentas | **Depurador** | Mostra linha a linha e coluna a coluna o que cada leitor aproveita, alertas e inconsistências de preenchimento |

Também: **menu inicial** com busca ("O que você quer fazer?"), **menu lateral recolhível**, modo claro/escuro,
configurações (desativar abas, backup automático, exportar/importar dados) e layout para celular.

## Onde ficam os dados

O original gravava `historico_qualidade.json`, `preferencias.json` e `pareceres_padrao.json` numa pasta.
No QSmart esses mesmos dados (no mesmo formato) ficam guardados **no navegador** (IndexedDB):

- **Backup automático** ao abrir, ao gravar e ao sair, sem repetir quando nada mudou (configurável: manter os últimos N).
- **Exportar dados** gera um `.json` com tudo — serve para guardar fora do navegador ou levar para outro computador.
- **Importar dados** aceita esse pacote **e os arquivos do programa anterior**, então o histórico antigo pode ser trazido direto.

> Os dados ficam no navegador e no computador em que foram gravados. Para usar em outra máquina, exporte e importe.

## Arquitetura

```
src/
  QSmart.Core/   Lógica (C# puro, sem dependências): leitor xlsx/csv, Resumo, Evolução, Histórico,
                 Pareceres (difflib portado), Inspeção, Volume, Corredor, Monitor, Depurador,
                 gravador de Excel e gerador de PDF
  QSmart.UI/     Interface (componentes Razor): menu, páginas, gráficos, tabelas, diálogos, tema
  QSmart.Web/    Aplicação Blazor WebAssembly publicada no GitHub Pages
tools/DevHost/   Servidor local de desenvolvimento (Blazor Server) — não é publicado
assets/brand/    Logo e símbolo da marca
```

Bibliotecas de terceiros usadas só no navegador: [pdf-lib](https://pdf-lib.js.org) (juntar PDFs, MIT),
[PDF.js](https://mozilla.github.io/pdf.js/) (ler texto de PDF de pareceres, Apache-2.0) e a fonte
[Inter](https://rsms.me/inter/) (OFL). Tudo é servido junto com o site.

A lógica foi conferida contra o programa original em Python: com as mesmas planilhas de teste, os números de
todas as abas, as exportações Excel e os relatórios PDF batem.

## Desenvolver

Requer o SDK do .NET 8.

```bash
# rodar a versão WebAssembly (igual à publicada)
dotnet run --project src/QSmart.Web

# ou o servidor local de desenvolvimento
dotnet run --project tools/DevHost -p:QSmartLocal=true
```

## Publicar

Todo push na branch `main` dispara o workflow [`Publicar no GitHub Pages`](.github/workflows/publicar.yml):
compila o `QSmart.Web`, ajusta o endereço base para `/QSmart/` e publica no GitHub Pages
(Settings → Pages → Source: **GitHub Actions**).

## Identidade visual

<img src="assets/brand/qsmart-simbolo.svg" width="72" align="right" alt="Símbolo QSmart">

- **Símbolo:** um **Q** cujo interior é um **check de conferido**, com uma **faísca** — qualidade verificada com inteligência.
  A forma também lembra uma lupa, ligando à inspeção.
- **Nome:** **Q**Smart, com o “Q” em gradiente; assinatura **BY INSI** em caixa alta espaçada.
- **Cores da marca:** Índigo Insi `#4F46E5` → Azul `#4361EE` → Ciano Smart `#06B6D4` (gradiente diagonal);
  base escura do menu `#0E1230`.
- **Cores de dados** (mantidas do original, pelo significado): aprovação `#16A34A`, reprovação `#DC2626`,
  Interno `#0EA5E9`, Externo `#D97706`, Chamados `#8B5CF6`.
- **Tipografia:** Inter (variável), números tabulares nos indicadores e tabelas.
- **Arquivos:** [`qsmart-simbolo.svg`](assets/brand/qsmart-simbolo.svg),
  [`qsmart-logo-horizontal.svg`](assets/brand/qsmart-logo-horizontal.svg),
  [`qsmart-logo-horizontal-negativo.svg`](assets/brand/qsmart-logo-horizontal-negativo.svg) (para fundo escuro).

---

<p align="center"><sub>QSmart · Insi</sub></p>
