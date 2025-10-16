# SimulateAnDb

Este é um repositório para um projeto de simulação de banco de dados hisórico do Action.Net Scada. 
O objetivo é criar um banco de dados que imite o comportamento e a estrutura do banco de dados utilizado pelo Action.Net Scada, 
permitindo testes e desenvolvimento sem a necessidade do software original.

# Tecnologias Utilizadas

- .Net 9.0;
- Dapper;
- Multiplicidade bancos de dados para escolha (SQL Server, PostgreSQL, SQLite);
- Spectre.Console para interface de linha de comando interativa.

# Funcionalidades

- Busca da estrutura de tabela do banco de dados do Action.Net Scada a partir do nome da tabela;
- Simulação de valores históricos (em curva senoidal não estrita) para as tabelas do banco de dados a partir de uma data inicial, uma data final e o intervalo entre o salvamento dos valores;
- Interface de linha de comando interativa para facilitar a configuração e execução das simulações;
- Suporte a múltiplos bancos de dados (SQL Server, PostgreSQL, SQLite).
- Configuração via arquivo appsettings.json.
- Logs detalhados para monitoramento e depuração.

# Como Usar

1- Clone o repositório;
2- Configure o arquivo `appsettings.json` com as informações do seu banco de dados;
3- Execute o projeto e siga as instruções na interface de linha de comando para iniciar a simulação.

## Exemplo de Configuração do appsettings.json

```json
  "ConnectionStrings": {
    "Default": "Provider=SQLite;Data Source=TestFilterV21.dbHistorian;"
  },
  "Simulation": {
    "CommitBatchSize": 1000,
    "ValueMin": 40.0,
    "ValueMax": 95.0,
    "NoiseAmplitude": 0.5
  }
}
```

Onde:
- `ConnectionStrings:Default`: Define a string de conexão com o banco de dados. Exemplo para SQLite, SQL Server ou PostgreSQL.
- `Simulation:CommitBatchSize`: Define o tamanho do lote para commits no banco de dados.
- `Simulation:ValueMin`: Define o valor mínimo para a simulação.
- `Simulation:ValueMax`: Define o valor máximo para a simulação.
- `Simulation:NoiseAmplitude`: Define a amplitude do ruído adicionado aos valores simulados.

## Exemplo de comando

```bash
dotnet run -- --table Ana -s 2023-01-01 -e 2023-01-02 --interval 5
```

Onde: 
- `--table <valor>` ou `-t <valor>`: Especifica o nome da tabela a ser simulada (exemplo: Ana);
- `--interval <valor>` ou `-i <valor>`: Especifica o intervalo em minutos entre cada registro simulado (exemplo: 5).
- `--startdate <valor>` ou `-s <valor>`: Especifica a data inicial da simulação no formato DD-MM-YYYY (exemplo: 01-10-2025).
- `--enddate <valor>` ou `-e <valor>`: Especifica a data final da simulação no formato DD-MM-YYYY (exemplo: 03-10-2025).
