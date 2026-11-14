# Validación

## Verificación realizada

- Compilación Release con .NET 10 para Windows x64.
- Pruebas automáticas del modo de demostración: autenticación, persistencia, unicidad, integridad de relaciones y exportación Excel.
- Renderizado e inspección visual de inicio, tabla de equipos, reportes, edición de equipo y primer acceso.

No se ha probado la conexión a una instancia real de SQL Server ni la impresión en una impresora física en este entorno.

## Comprobación con SQL Server

Usar una base de pruebas vacía, sin datos de producción.

1. Ejecutar `database/01-schema.sql` dos veces: no debe borrar datos ni volver a crear tablas existentes.
2. Seleccionar el origen SQL Server y crear el primer administrador.
3. Crear una ubicación y un responsable. Registrar un equipo asignado a ambos.
4. Cerrar y abrir la aplicación: los registros y el acceso deben conservarse.
5. Intentar duplicar código y serie: debe rechazarse sin modificar el registro original.
6. Intentar eliminar una ubicación asignada: debe rechazarse. Reasignar el equipo y volver a intentar.
7. Registrar un mantenimiento, verificarlo en reportes y probar su persistencia.
8. Exportar un reporte filtrado y abrirlo en Excel o LibreOffice; comprobar tildes, códigos con ceros iniciales y cantidades.
9. Crear un encargado: debe poder gestionar registros y consultar reportes, sin administrar usuarios ni eliminar registros.
10. Intentar eliminar o desactivar al último administrador: debe rechazarse.
11. Con dos sesiones, editar el mismo registro: la segunda actualización debe detectar el conflicto y pedir actualizar la lista.
12. Desconectar el servidor durante un guardado: el cambio no debe aparecer como confirmado. Recuperar conexión y actualizar la tabla.
13. Generar un reporte de varias páginas y comprobar la vista previa y una impresión PDF.

## Copias de seguridad

En demostración, cerrar la aplicación y copiar `demo.xml` desde `%LOCALAPPDATA%\InventarioSanMarcos`. En SQL Server, usar las herramientas de respaldo de la instancia. Las copias pueden contener datos personales y hashes de contraseñas: deben mantenerse fuera de GitHub.
