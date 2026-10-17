// Builds the Store screenshot templates as layered PSDs (amber and slate) next to this script, in templates\:
// gradient background, live text layers for the caption and subline, the accent bar, and a smart object as the
// screenshot placeholder with a drop shadow (Layer > Smart Objects > Replace Contents keeps size and shadow).
// Same layout as Compose-StoreShot.ps1. Run in Photoshop: File > Scripts > Browse...
#target photoshop

var W = 2560, H = 1440;
var SCHEMES = [
    { name: 'amber', from: 'fbe7b8', to: 'f0a93e', text: '1c2140', sub: '3a4166', accent: '3a4470' },
    { name: 'slate', from: '232848', to: '545d7e', text: 'ffffff', sub: 'd3d7ee', accent: 'ff8c42' }
];
var CAPTION = 'Your text templates, in one place';
var SUBLINE = 'Folders, rich text, tags and shortcuts for every reply you write again and again';
// The main window capture (1684x1055) fitted into the composer's box, 80 % x 70 % of the canvas.
var SHOT = { w: 1609, h: 1008, x: 475, y: 340 };

function cid(id) { return charIDToTypeID(id); }
function sid(id) { return stringIDToTypeID(id); }

function rgb(hex) {
    var col = new SolidColor();
    col.rgb.hexValue = hex;
    return col;
}

function colorDesc(hex) {
    var d = new ActionDescriptor();
    d.putDouble(cid('Rd  '), parseInt(hex.substr(0, 2), 16));
    d.putDouble(cid('Grn '), parseInt(hex.substr(2, 2), 16));
    d.putDouble(cid('Bl  '), parseInt(hex.substr(4, 2), 16));
    return d;
}

// A gradient fill layer from the top left (from) to the bottom right (to).
function gradientLayer(from, to) {
    function colorStop(hex, location) {
        var d = new ActionDescriptor();
        d.putObject(cid('Clr '), cid('RGBC'), colorDesc(hex));
        d.putEnumerated(cid('Type'), cid('Clry'), cid('UsrS'));
        d.putInteger(cid('Lctn'), location);
        d.putInteger(cid('Mdpn'), 50);
        return d;
    }
    function opacityStop(location) {
        var d = new ActionDescriptor();
        d.putUnitDouble(cid('Opct'), cid('#Prc'), 100);
        d.putInteger(cid('Lctn'), location);
        d.putInteger(cid('Mdpn'), 50);
        return d;
    }
    var grad = new ActionDescriptor();
    grad.putString(cid('Nm  '), 'Background');
    grad.putEnumerated(cid('GrdF'), cid('GrdF'), cid('CstS'));
    grad.putDouble(cid('Intr'), 4096);
    var colors = new ActionList();
    colors.putObject(cid('Clrt'), colorStop(from, 0));
    colors.putObject(cid('Clrt'), colorStop(to, 4096));
    grad.putList(cid('Clrs'), colors);
    var opacity = new ActionList();
    opacity.putObject(cid('TrnS'), opacityStop(0));
    opacity.putObject(cid('TrnS'), opacityStop(4096));
    grad.putList(cid('Trns'), opacity);

    var fill = new ActionDescriptor();
    fill.putUnitDouble(cid('Angl'), cid('#Ang'), -45);
    fill.putEnumerated(cid('Type'), cid('GrdT'), cid('Lnr '));
    fill.putObject(cid('Grad'), cid('Grdn'), grad);
    var layer = new ActionDescriptor();
    layer.putObject(cid('Type'), sid('gradientLayer'), fill);
    var make = new ActionDescriptor();
    var ref = new ActionReference();
    ref.putClass(sid('contentLayer'));
    make.putReference(cid('null'), ref);
    make.putObject(cid('Usng'), sid('contentLayer'), layer);
    executeAction(cid('Mk  '), make, DialogModes.NO);
}

function dropShadow(opacity, distance, size) {
    var shadow = new ActionDescriptor();
    shadow.putBoolean(cid('enab'), true);
    shadow.putEnumerated(cid('Md  '), cid('BlnM'), cid('Mltp'));
    shadow.putObject(cid('Clr '), cid('RGBC'), colorDesc('000000'));
    shadow.putUnitDouble(cid('Opct'), cid('#Prc'), opacity);
    shadow.putBoolean(cid('uglg'), false);
    shadow.putUnitDouble(cid('lagl'), cid('#Ang'), 90);
    shadow.putUnitDouble(cid('Dstn'), cid('#Pxl'), distance);
    shadow.putUnitDouble(cid('Ckmt'), cid('#Pxl'), 0);
    shadow.putUnitDouble(cid('blur'), cid('#Pxl'), size);
    var fx = new ActionDescriptor();
    fx.putUnitDouble(cid('Scl '), cid('#Prc'), 100);
    fx.putObject(cid('DrSh'), cid('DrSh'), shadow);
    var set = new ActionDescriptor();
    var ref = new ActionReference();
    ref.putProperty(cid('Prpr'), cid('Lefx'));
    ref.putEnumerated(cid('Lyr '), cid('Ordn'), cid('Trgt'));
    set.putReference(cid('null'), ref);
    set.putObject(cid('T   '), cid('Lefx'), fx);
    executeAction(cid('setd'), set, DialogModes.NO);
}

// A shape layer with fully rounded ends (radius = half the height).
function roundedBar(x, y, w, h, hex) {
    var rect = new ActionDescriptor();
    rect.putUnitDouble(cid('Top '), cid('#Pxl'), y);
    rect.putUnitDouble(cid('Left'), cid('#Pxl'), x);
    rect.putUnitDouble(cid('Btom'), cid('#Pxl'), y + h);
    rect.putUnitDouble(cid('Rght'), cid('#Pxl'), x + w);
    rect.putUnitDouble(cid('Rds '), cid('#Pxl'), h / 2);
    var solid = new ActionDescriptor();
    solid.putObject(cid('Clr '), cid('RGBC'), colorDesc(hex));
    var layer = new ActionDescriptor();
    layer.putObject(cid('Type'), sid('solidColorLayer'), solid);
    layer.putObject(cid('Shp '), cid('Rctn'), rect);
    var make = new ActionDescriptor();
    var ref = new ActionReference();
    ref.putClass(sid('contentLayer'));
    make.putReference(cid('null'), ref);
    make.putObject(cid('Usng'), sid('contentLayer'), layer);
    executeAction(cid('Mk  '), make, DialogModes.NO);
}

function fillRect(doc, layer, x, y, w, h, hex) {
    app.activeDocument = doc;
    doc.activeLayer = layer;
    doc.selection.select([[x, y], [x + w, y], [x + w, y + h], [x, y + h]]);
    doc.selection.fill(rgb(hex));
    doc.selection.deselect();
}

function textLayer(doc, name, text, font, size, hex, baseline) {
    var layer = doc.artLayers.add();
    layer.kind = LayerKind.TEXT;
    layer.name = name;
    var t = layer.textItem;
    t.contents = text;
    t.font = font;
    t.size = new UnitValue(size, 'px');
    t.color = rgb(hex);
    t.justification = Justification.CENTER;
    t.position = [new UnitValue(W / 2, 'px'), new UnitValue(baseline, 'px')];
    return layer;
}

function buildTemplate(scheme, folder) {
    var doc = app.documents.add(W, H, 72, 'store-template-' + scheme.name, NewDocumentMode.RGB, DocumentFill.TRANSPARENT);
    var empty = doc.activeLayer;

    gradientLayer(scheme.from, scheme.to);
    doc.activeLayer.name = 'Background';

    roundedBar(W / 2 - 60, 190, 120, 8, scheme.accent);
    doc.activeLayer.name = 'Accent bar';

    textLayer(doc, 'Caption', CAPTION, 'SegoeUI-Semibold', 76, scheme.text, 168);
    textLayer(doc, 'Subline', SUBLINE, 'SegoeUI', 38, scheme.sub, 261);

    // The placeholder: a grey card with a hint, turned into a smart object for Replace Contents.
    var card = doc.artLayers.add();
    card.name = 'Screenshot';
    fillRect(doc, card, SHOT.x, SHOT.y, SHOT.w, SHOT.h, 'e4e6ee');
    var hint = textLayer(doc, 'Hint', 'Screenshot: Layer > Smart Objects > Replace Contents', 'SegoeUI', 40, '6b7090', SHOT.y + SHOT.h / 2 + 14);
    hint.merge();
    doc.activeLayer.name = 'Screenshot';
    executeAction(sid('newPlacedLayer'), undefined, DialogModes.NO);
    var shot = doc.activeLayer;
    shot.name = 'Screenshot (Replace Contents)';
    dropShadow(55, 16, 56);

    // Removing the document's first (empty) layer works but then throws: "Select" is not available.
    try { empty.remove(); } catch (e) { }
    var options = new PhotoshopSaveOptions();
    options.layers = true;
    options.embedColorProfile = true;
    doc.saveAs(new File(folder + '/store-template-' + scheme.name + '.psd'), options, true, Extension.LOWERCASE);
    doc.close(SaveOptions.DONOTSAVECHANGES);
}

var units = app.preferences.rulerUnits, typeUnits = app.preferences.typeUnits, dialogs = app.displayDialogs;
app.preferences.rulerUnits = Units.PIXELS;
app.preferences.typeUnits = TypeUnits.PIXELS;
app.displayDialogs = DialogModes.NO;
try {
    var folder = new Folder(File($.fileName).parent + '/templates');
    if (!folder.exists) folder.create();
    for (var i = 0; i < SCHEMES.length; i++) buildTemplate(SCHEMES[i], folder.fsName.replace(/\\/g, '/'));
} finally {
    app.preferences.rulerUnits = units;
    app.preferences.typeUnits = typeUnits;
    app.displayDialogs = dialogs;
}
