/*
    Las tablas legacy no tienen una clave primaria natural inequívoca.
    Se conservan sin PK y con los índices definidos por el sistema original.
    Los Double monetarios, porcentajes y cantidades se convierten a decimal(18,4).
    Los Double usados como identificadores se convierten a decimal(18,0).
*/

IF OBJECT_ID(N'dbo.HORAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HORAS
    (
        HOR_ID nvarchar(30) NULL,
        HOR_ID2 nvarchar(30) NULL,
        HOR_EMP int NULL,
        HOR_EMPRESA int NULL,
        HOR_TIENDA int NULL,
        HOR_FECHA datetime2 NULL,
        HOR_FECHA2 datetime2 NULL,
        HOR_TURNO int NULL,
        HOR_HORAS decimal(18,4) NULL,
        HOR_HORAS_EXTRAS decimal(18,4) NULL,
        HOR_DESCANSO int NULL,
        HOR_OBSERVACIO nvarchar(max) NULL,
        HOR_LIN int NULL,
        HOR_ENTRADA nvarchar(19) NULL,
        HOR_SALIDA nvarchar(19) NULL,
        HOR_IN1 nvarchar(8) NULL,
        HOR_OUT1 nvarchar(8) NULL,
        FOTO varbinary(max) NULL,
        FOTOS varbinary(max) NULL,
        OPERADOR int NULL,
        OPERADOR_FECHA datetime2 NULL,
        id_internet nvarchar(29) NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'FECHA')
    CREATE INDEX FECHA ON dbo.HORAS (HOR_FECHA);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'USUARIO')
    CREATE INDEX USUARIO ON dbo.HORAS (HOR_EMPRESA, HOR_TIENDA, HOR_EMP, HOR_FECHA DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'USUARIO2')
    CREATE INDEX USUARIO2 ON dbo.HORAS (HOR_EMPRESA, HOR_TIENDA, HOR_EMP, HOR_FECHA, HOR_IN1);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'USUARIO3')
    CREATE INDEX USUARIO3 ON dbo.HORAS (HOR_EMPRESA, HOR_EMP, HOR_FECHA, HOR_IN1);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'usuario4')
    CREATE INDEX usuario4 ON dbo.HORAS (HOR_EMPRESA, HOR_EMP, HOR_FECHA);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'HOR_EMP')
    CREATE INDEX HOR_EMP ON dbo.HORAS (HOR_EMP);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'id_internet')
    CREATE INDEX id_internet ON dbo.HORAS (id_internet);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'hor_id')
    CREATE INDEX hor_id ON dbo.HORAS (HOR_ID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HORAS') AND name = N'hor_id2')
    CREATE INDEX hor_id2 ON dbo.HORAS (HOR_ID2);
GO

IF OBJECT_ID(N'dbo.pedidos_tmp', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.pedidos_tmp
    (
        ID_TIQUETL nvarchar(24) NULL,
        ID_tienda int NULL,
        almacen int NULL,
        ID_CAIXA int NULL,
        ID_USUARI int NULL,
        tiempo decimal(18,4) NULL,
        comision decimal(18,4) NULL,
        ID_TARJACLIENT nvarchar(16) NULL,
        ID_TAULA smallint NULL,
        ID_SALA smallint NULL,
        ID_EMPRESA smallint NULL,
        ID_ARQUEIG decimal(18,0) NULL,
        ID_ARTICLE nvarchar(13) NULL,
        ID_IVA smallint NULL,
        ID_TARIFA smallint NULL,
        ID_FORMAT smallint NULL,
        DESCRIPCIO nvarchar(30) NULL,
        QUANTITAT decimal(18,4) NULL,
        MESURA decimal(18,4) NULL,
        ESPERPES bit NULL,
        NUMPERSONES decimal(18,4) NULL,
        PVC decimal(18,4) NULL,
        DESCOMPTE decimal(18,4) NULL,
        PREUPERPES decimal(18,4) NULL,
        PREU decimal(18,4) NULL,
        PREUAMBIVA decimal(18,4) NULL,
        PREUTARIFA decimal(18,4) NULL,
        PERCENTIVA decimal(18,4) NULL,
        PERCENTREQ decimal(18,4) NULL,
        HORA_INICI nvarchar(8) NULL,
        COMENTARIS nvarchar(max) NULL,
        OBSERVACIO nvarchar(max) NULL,
        BASE decimal(18,4) NULL,
        TOTALDTE decimal(18,4) NULL,
        TOTAL decimal(18,4) NULL,
        CVALIVA decimal(18,4) NULL,
        CVALREQ decimal(18,4) NULL,
        DATAASERVIR datetime2 NULL,
        HORAASERVIR nvarchar(8) NULL,
        ID_proveedor int NULL,
        ESENCARGO bit NULL,
        ID_CLIENTENV nvarchar(10) NULL,
        ID_ARTICLETC nvarchar(13) NULL,
        ID_COLOR int NULL,
        DESCCOLOR nvarchar(30) NULL,
        DESCMARCA nvarchar(30) NULL,
        TALLA nvarchar(10) NULL,
        ID_MARCA int NULL,
        FOTOMARCA varbinary(max) NULL,
        OCULTARCTD bit NULL,
        NOIMPRIMIR bit NULL,
        ID_TIQUETLMASTER nvarchar(24) NULL,
        JAIMPRESACUINA bit NULL,
        PRINTACUINA bit NULL,
        PRINTABARRA bit NULL,
        TEMODIFICADORS bit NULL,
        TEOPCIONSKIT bit NULL,
        ESKIT bit NULL,
        ESMODIFICADOR bit NULL,
        ORDRELINIA decimal(18,4) NULL,
        tipo_mesa nvarchar(20) NULL,
        esimpreso bit NULL,
        id_artcentral nvarchar(13) NULL,
        id_familia nvarchar(13) NULL,
        marcado bit NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.pedidos_tmp') AND name = N'id_usuari')
    CREATE INDEX id_usuari ON dbo.pedidos_tmp (ID_USUARI);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.pedidos_tmp') AND name = N'id_tiquetl')
    CREATE INDEX id_tiquetl ON dbo.pedidos_tmp (ID_TIQUETL, ID_TIQUETLMASTER);
GO

IF OBJECT_ID(N'dbo.pedidos_tmpp', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.pedidos_tmpp
    (
        ID_TIQUETL nvarchar(24) NULL,
        ID_tienda int NULL,
        almacen int NULL,
        ID_CAIXA int NULL,
        ID_USUARI int NULL,
        tiempo decimal(18,4) NULL,
        comision decimal(18,4) NULL,
        ID_TARJACLIENT nvarchar(16) NULL,
        ID_TAULA smallint NULL,
        ID_SALA smallint NULL,
        ID_EMPRESA smallint NULL,
        ID_ARQUEIG decimal(18,0) NULL,
        ID_ARTICLE nvarchar(13) NULL,
        ID_IVA smallint NULL,
        ID_TARIFA smallint NULL,
        ID_FORMAT smallint NULL,
        DESCRIPCIO nvarchar(30) NULL,
        QUANTITAT decimal(18,4) NULL,
        MESURA decimal(18,4) NULL,
        ESPERPES bit NULL,
        NUMPERSONES decimal(18,4) NULL,
        PVC decimal(18,4) NULL,
        DESCOMPTE decimal(18,4) NULL,
        PREUPERPES decimal(18,4) NULL,
        PREU decimal(18,4) NULL,
        PREUAMBIVA decimal(18,4) NULL,
        PREUTARIFA decimal(18,4) NULL,
        PERCENTIVA decimal(18,4) NULL,
        PERCENTREQ decimal(18,4) NULL,
        HORA_INICI nvarchar(8) NULL,
        COMENTARIS nvarchar(max) NULL,
        OBSERVACIO nvarchar(max) NULL,
        BASE decimal(18,4) NULL,
        TOTALDTE decimal(18,4) NULL,
        TOTAL decimal(18,4) NULL,
        CVALIVA decimal(18,4) NULL,
        CVALREQ decimal(18,4) NULL,
        DATAASERVIR datetime2 NULL,
        HORAASERVIR nvarchar(8) NULL,
        ID_proveedor int NULL,
        ESENCARGO bit NULL,
        ID_CLIENTENV nvarchar(10) NULL,
        ID_ARTICLETC nvarchar(13) NULL,
        ID_COLOR int NULL,
        DESCCOLOR nvarchar(30) NULL,
        DESCMARCA nvarchar(30) NULL,
        TALLA nvarchar(10) NULL,
        ID_MARCA int NULL,
        FOTOMARCA varbinary(max) NULL,
        OCULTARCTD bit NULL,
        NOIMPRIMIR bit NULL,
        ID_TIQUETLMASTER nvarchar(24) NULL,
        JAIMPRESACUINA bit NULL,
        PRINTACUINA bit NULL,
        PRINTABARRA bit NULL,
        TEMODIFICADORS bit NULL,
        TEOPCIONSKIT bit NULL,
        ESKIT bit NULL,
        ESMODIFICADOR bit NULL,
        ORDRELINIA decimal(18,4) NULL,
        tipo_mesa nvarchar(20) NULL,
        esimpreso bit NULL,
        id_artcentral nvarchar(13) NULL,
        id_familia nvarchar(13) NULL,
        marcado bit NULL,
        id_internet nvarchar(18) NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.pedidos_tmpp') AND name = N'id_usuari')
    CREATE INDEX id_usuari ON dbo.pedidos_tmpp (ID_USUARI);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.pedidos_tmpp') AND name = N'id_tiquetl')
    CREATE INDEX id_tiquetl ON dbo.pedidos_tmpp (ID_TIQUETL, ID_TIQUETLMASTER);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.pedidos_tmpp') AND name = N'dataaservir')
    CREATE INDEX dataaservir ON dbo.pedidos_tmpp (DATAASERVIR);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.pedidos_tmpp') AND name = N'ordrelinia')
    CREATE INDEX ordrelinia ON dbo.pedidos_tmpp (ORDRELINIA);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.pedidos_tmpp') AND name = N'id_internet')
    CREATE INDEX id_internet ON dbo.pedidos_tmpp (id_internet);
GO

IF OBJECT_ID(N'dbo.tiquetssii', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tiquetssii
    (
        contador_sii decimal(18,0) NULL,
        mac_dispositivo nvarchar(30) NULL,
        n_serie_imei_dispositivo nvarchar(100) NULL,
        version nvarchar(20) NULL,
        id_anyo decimal(18,0) NULL,
        id_empresa int NULL,
        id_tienda int NULL,
        id_caixa int NULL,
        id_serie smallint NULL,
        id_tiquet decimal(18,0) NULL,
        id_usuari int NULL,
        hora_inicio nvarchar(8) NULL,
        hora_final nvarchar(8) NULL,
        id_client nvarchar(10) NULL,
        fecha datetime2 NULL,
        id_sala smallint NULL,
        id_taula smallint NULL,
        tipo_mesa nvarchar(20) NULL,
        id_tisi nvarchar(255) NULL,
        texto_qr nvarchar(max) NULL,
        tiquet_sii nvarchar(max) NULL,
        firma_sii nvarchar(max) NULL,
        id_anyo_ant decimal(18,0) NULL,
        id_empresa_ant int NULL,
        id_tienda_ant int NULL,
        id_caixa_ant int NULL,
        id_serie_ant smallint NULL,
        id_tiquet_ant decimal(18,0) NULL,
        firma_sii_ant nvarchar(max) NULL,
        tiquet_sii_sin_firmar nvarchar(max) NULL,
        firmado_sii bit NULL,
        enviado_sii bit NULL,
        enviado_sin_firmar_cloud bit NULL,
        fecha_hora_sin_firmar_cloud nvarchar(20) NULL,
        respuesta_sin_firmar_cloud nvarchar(max) NULL,
        enviado_firmado_cloud bit NULL,
        fecha_hora_firmado_cloud nvarchar(20) NULL,
        respuesta_firmado_cloud nvarchar(max) NULL,
        codigo_ejecutar_firma nvarchar(max) NULL,
        codigo_ejecutar_envio nvarchar(max) NULL,
        cabecera_respuesta nvarchar(max) NULL,
        fichero_respuesta nvarchar(max) NULL,
        fecha_hora_recepcion nvarchar(50) NULL,
        estado_recepcion nvarchar(5) NULL,
        descripcion_recepcion nvarchar(max) NULL,
        codigo_resultado_validacion nvarchar(5) NULL,
        descripcion_validacion nvarchar(max) NULL,
        id_internet nvarchar(18) NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'contador_sii')
    CREATE INDEX contador_sii ON dbo.tiquetssii (contador_sii);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'ENVIADO_FIRMADO_CLOUD')
    CREATE INDEX ENVIADO_FIRMADO_CLOUD ON dbo.tiquetssii (enviado_firmado_cloud);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'ENVIADO_SIN_FIRMAR_CLOUD')
    CREATE INDEX ENVIADO_SIN_FIRMAR_CLOUD ON dbo.tiquetssii (enviado_sin_firmar_cloud);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'ENVIADO_SII')
    CREATE INDEX ENVIADO_SII ON dbo.tiquetssii (enviado_sii);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'fecha')
    CREATE INDEX fecha ON dbo.tiquetssii (fecha);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'id_caixa')
    CREATE INDEX id_caixa ON dbo.tiquetssii (id_caixa);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'id_empresa')
    CREATE INDEX id_empresa ON dbo.tiquetssii (id_empresa);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'id_sala')
    CREATE INDEX id_sala ON dbo.tiquetssii (id_sala);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'id_usuari')
    CREATE INDEX id_usuari ON dbo.tiquetssii (id_usuari);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'id_internet')
    CREATE INDEX id_internet ON dbo.tiquetssii (id_internet);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'tiquet')
    CREATE UNIQUE INDEX tiquet ON dbo.tiquetssii
    (
        id_anyo,
        id_empresa,
        id_tienda,
        id_caixa,
        id_serie,
        id_tiquet
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tiquetssii') AND name = N'dispositivo')
    CREATE UNIQUE INDEX dispositivo ON dbo.tiquetssii
    (
        mac_dispositivo,
        n_serie_imei_dispositivo,
        contador_sii
    );
GO
