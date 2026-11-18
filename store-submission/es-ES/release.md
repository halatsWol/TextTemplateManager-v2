### Novedades de esta versión

#### Nuevo

- **Plantillas en varios idiomas:** una plantilla ahora puede tener su texto en varios idiomas, uno por pestaña sobre la barra de herramientas del editor, con un solo título y un solo conjunto de abreviaturas. **+** añade un idioma (reconocido a partir del texto cuando es posible) y **Traducir ▸ Añadir como idioma** traduce a una nueva pestaña. La primera pestaña es la predeterminada, la que se pega; **Copiar idioma** en el menú del clic derecho de la lista, **Pegar idioma** en Quick Paste y **← / →** en la lista de secuencias de teclas de Quick Paste eligen otro. La búsqueda abarca todos los idiomas.
- **Extensión del navegador:** con TTM-Connect 1.2 o posterior también puedes pegar los demás idiomas de una plantilla y guardar una selección como idioma de una plantilla. Las versiones anteriores de la extensión siguen funcionando como antes.
- **Carpetas sincronizadas:** las plantillas en varios idiomas necesitan TextTemplateManager 2.7 o posterior en cada equipo que guarda un archivo compartido; TextTemplateManager 1 y las versiones 2.6 o anteriores eliminan los demás idiomas al guardarlo.

#### Mejorado

- **Funciona con .NET 10:** TextTemplateManager ahora usa .NET 10, para el que Microsoft ofrece actualizaciones de seguridad hasta noviembre de 2028. .NET 8, que usaban las versiones anteriores, ya no las recibe desde el 10 de noviembre de 2026. Tus plantillas, tu configuración y tu forma de trabajar siguen igual.
- **Iconos más claros:** las carpetas y las plantillas se distinguen mejor en la lista y en los botones para añadirlas.

#### Corregido

- **Archivos compartidos:** guardar en un archivo sincronizado ya no sobrescribe los cambios que otro equipo hizo desde que se leyó; se combinan en la siguiente comprobación.
- **Salir:** al salir, la aplicación ahora espera a que termine un guardado de la configuración ya en curso.