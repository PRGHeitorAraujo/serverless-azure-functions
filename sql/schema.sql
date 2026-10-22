-- Tabela usada pelas funções ListToDos / GetToDo / CreateToDo.
-- O binding de SQL exige chave primária na tabela de destino.

IF OBJECT_ID(N'dbo.ToDo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ToDo
    (
        [Id]        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [Order]     INT              NULL,
        [Title]     NVARCHAR(200)    NOT NULL,
        [Url]       NVARCHAR(2048)   NULL,
        [Completed] BIT              NOT NULL DEFAULT 0
    );
END
GO
