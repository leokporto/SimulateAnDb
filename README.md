# SimulateAnDb

Este � um reposit�rio para um projeto de simula��o de banco de dados his�rico do Action.Net Scada. 
O objetivo � criar um banco de dados que imite o comportamento e a estrutura do banco de dados utilizado pelo Action.Net Scada, 
permitindo testes e desenvolvimento sem a necessidade do software original.

>[!WARNING]
>
>Este aplicativo foi desenvolvido com o intuito de atualizar bases de dados do Action.Net. Para que funcione da forma esperada, utilize apenas este tipo de base.
>



# Tecnologias Utilizadas

- .Net 9.0;
- Dapper;
- Multiplicidade bancos de dados para escolha (SQL Server, PostgreSQL, SQLite);
- Spectre.Console para interface de linha de comando interativa.

# Funcionalidades

- Busca da estrutura de tabela do banco de dados do Action.Net Scada a partir do nome da tabela;
- Simula��o de valores hist�ricos (em curva senoidal n�o estrita) para as tabelas do banco de dados a partir de uma data inicial, uma data final e o intervalo entre o salvamento dos valores;
- Interface de linha de comando interativa para facilitar a configura��o e execu��o das simula��es;
- Suporte a m�ltiplos bancos de dados (SQL Server, PostgreSQL, SQLite).
- Configura��o via arquivo appsettings.json.
- Logs detalhados para monitoramento e depura��o.

# Como Usar

1- Clone o reposit�rio;
2- Configure o arquivo `appsettings.json` com as informa��es do seu banco de dados;
3- Execute o projeto e siga as instru��es na interface de linha de comando para iniciar a simula��o.

## Exemplo de Configura��o do appsettings.json

```json
{
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
- `ConnectionStrings:Default`: Define a string de conex�o com o banco de dados. Exemplo para SQLite, SQL Server ou PostgreSQL.
- `Simulation:CommitBatchSize`: Define o tamanho do lote para commits no banco de dados.
- `Simulation:ValueMin`: Define o valor m�nimo para a simula��o.
- `Simulation:ValueMax`: Define o valor m�ximo para a simula��o.
- `Simulation:NoiseAmplitude`: Define a amplitude do ru�do adicionado aos valores simulados.

## Exemplo de comando

```bash
dotnet run -- --table Ana -s 2023-01-01 -e 2023-01-02 --interval 5
```

Onde: 
- `--table <valor>` ou `-t <valor>`: Especifica o nome da tabela a ser simulada (exemplo: Ana);
- `--interval <valor>` ou `-i <valor>`: Especifica o intervalo em minutos entre cada registro simulado (exemplo: 5).
- `--startdate <valor>` ou `-s <valor>`: Especifica a data inicial da simula��o no formato DD-MM-YYYY (exemplo: 01-10-2025).
- `--enddate <valor>` ou `-e <valor>`: Especifica a data final da simula��o no formato DD-MM-YYYY (exemplo: 03-10-2025).
