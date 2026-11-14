/* Ejecutar con SQL Server Management Studio o sqlcmd.
   No elimina ni reemplaza tablas existentes. */
IF DB_ID(N'InventarioSanMarcos') IS NULL
    CREATE DATABASE InventarioSanMarcos;
GO
USE InventarioSanMarcos;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Ubicaciones', N'U') IS NULL
CREATE TABLE dbo.Ubicaciones (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Nombre nvarchar(150) NOT NULL UNIQUE,
    Descripcion nvarchar(1000) NULL
);
IF OBJECT_ID(N'dbo.Responsables', N'U') IS NULL
CREATE TABLE dbo.Responsables (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Nombre nvarchar(150) NOT NULL,
    Documento nvarchar(150) NULL,
    Cargo nvarchar(150) NULL,
    Contacto nvarchar(150) NULL
);
IF OBJECT_ID(N'dbo.Equipos', N'U') IS NULL
BEGIN
CREATE TABLE dbo.Equipos (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Codigo nvarchar(150) NOT NULL UNIQUE,
    Tipo nvarchar(150) NOT NULL CHECK (Tipo IN (N'Computadora',N'Laptop',N'Monitor',N'Teclado',N'Mouse',N'Impresora',N'Proyector',N'Otro')),
    Marca nvarchar(150) NOT NULL,
    Modelo nvarchar(150) NOT NULL,
    Serie nvarchar(150) NULL,
    Procesador nvarchar(150) NULL,
    Ram nvarchar(150) NULL,
    Almacenamiento nvarchar(150) NULL,
    UbicacionId uniqueidentifier NOT NULL REFERENCES dbo.Ubicaciones(Id),
    ResponsableId uniqueidentifier NULL REFERENCES dbo.Responsables(Id),
    Estado nvarchar(150) NOT NULL CHECK (Estado IN (N'Operativo',N'Mantenimiento',N'De baja')),
    Observaciones nvarchar(1000) NULL
);
CREATE UNIQUE INDEX UX_Equipos_Serie ON dbo.Equipos(Serie) WHERE Serie IS NOT NULL;
CREATE INDEX IX_Equipos_Ubicacion ON dbo.Equipos(UbicacionId);
END;
IF OBJECT_ID(N'dbo.Mantenimientos', N'U') IS NULL
BEGIN
CREATE TABLE dbo.Mantenimientos (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    EquipoId uniqueidentifier NOT NULL REFERENCES dbo.Equipos(Id),
    Fecha datetime2 NOT NULL,
    Tipo nvarchar(150) NOT NULL CHECK (Tipo IN (N'Preventivo',N'Correctivo')),
    Tecnico nvarchar(150) NOT NULL,
    Detalle nvarchar(1000) NOT NULL,
    Costo decimal(12,2) NOT NULL CHECK (Costo >= 0),
    Estado nvarchar(150) NOT NULL CHECK (Estado IN (N'Pendiente',N'En proceso',N'Finalizado'))
);
CREATE INDEX IX_Mantenimientos_Equipo ON dbo.Mantenimientos(EquipoId,Fecha);
END;
IF OBJECT_ID(N'dbo.Usuarios', N'U') IS NULL
CREATE TABLE dbo.Usuarios (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Nombre nvarchar(150) NOT NULL,
    Login nvarchar(150) NOT NULL UNIQUE,
    Rol nvarchar(150) NOT NULL CHECK (Rol IN (N'Administrador',N'Encargado')),
    Activo bit NOT NULL,
    PasswordHash nvarchar(300) NOT NULL
);
COMMIT;
GO
