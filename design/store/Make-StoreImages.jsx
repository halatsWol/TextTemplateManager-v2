// Builds the Store screenshots (2560x1440 PNG) from the PSD templates in templates\ and the window captures in
// captures\: per colour scheme and image, the caption and subline are typed into the text layers, the capture
// replaces the smart object's contents and is fitted into the frame, and the result goes to <scheme>\.
// Run in Photoshop: File > Scripts > Browse...
#target photoshop

var W = 2560;
// The frame a capture is fitted into (never enlarged), centred, bottom edge where the template's placeholder ends.
var BOX = { w: 2048, h: 1008, bottom: 1348 };
var SCHEMES = ['amber', 'slate'];
var IMAGES = [
    { name: '01-main', shot: 'store-main', caption: 'Your text templates, in one place', sub: 'Folders, rich text, tags and shortcuts for every reply you write again and again' },
    { name: '02-quick-paste', shot: 'store-qp-single', caption: 'Paste any template with a single key', sub: 'Shift+Alt+Y opens Quick Paste in any app' },
    { name: '03-multi-key', shot: 'store-qp-multi', caption: 'Or hold Alt and type a short code', sub: 'Multi-key shortcuts narrow the list as you type' },
    { name: '04-paste-modes', shot: 'store-pastemode', caption: 'Pasted in the format the app needs', sub: 'Auto, HTML/Jira, HTML, RTF, Markdown or plain text' },
    { name: '05-search', shot: 'store-search', caption: 'Find any template as you type', sub: 'By title, tags or the text itself, fuzzy or strict' },
    { name: '06-sync', shot: 'store-sync', caption: 'Share templates with your team', sub: 'A synced file on OneDrive or a network share' },
    { name: '07-backups', shot: 'store-backups', caption: 'Automatic backups you can restore', sub: 'After your changes, every two hours and daily' },
    { name: '08-tour', shot: 'store-tour', caption: 'A guided tour gets you started', sub: 'On sample templates; yours stay untouched' }
];

function cid(id) { return charIDToTypeID(id); }
function sid(id) { return stringIDToTypeID(id); }

function replaceContents(file) {
    var d = new ActionDescriptor();
    d.putPath(cid('null'), file);
    executeAction(sid('placedLayerReplaceContents'), d, DialogModes.NO);
}

function px(v) { return v.as('px'); }

function fitIntoBox(layer) {
    var b = layer.boundsNoEffects;
    var w = px(b[2]) - px(b[0]), h = px(b[3]) - px(b[1]);
    var scale = Math.min(BOX.w / w, BOX.h / h, 1);
    if (scale < 1) layer.resize(scale * 100, scale * 100, AnchorPosition.MIDDLECENTER);
    b = layer.boundsNoEffects;
    w = px(b[2]) - px(b[0]);
    layer.translate((W - w) / 2 - px(b[0]), BOX.bottom - px(b[3]));
}

function buildImage(root, scheme, image) {
    var capture = new File(root + '/captures/' + image.shot + '.png');
    if (!capture.exists) return 'missing ' + capture.fsName;
    var doc = app.open(new File(root + '/templates/store-template-' + scheme + '.psd'));
    doc.layers.getByName('Caption').textItem.contents = image.caption;
    doc.layers.getByName('Subline').textItem.contents = image.sub;
    var shot = doc.layers.getByName('Screenshot (Replace Contents)');
    doc.activeLayer = shot;
    replaceContents(capture);
    fitIntoBox(doc.activeLayer);

    var folder = new Folder(root + '/' + scheme);
    if (!folder.exists) folder.create();
    var options = new PNGSaveOptions();
    options.compression = 9;
    doc.saveAs(new File(folder.fsName + '/' + image.name + '-' + scheme + '.png'), options, true, Extension.LOWERCASE);
    doc.close(SaveOptions.DONOTSAVECHANGES);
    return null;
}

var units = app.preferences.rulerUnits, dialogs = app.displayDialogs;
app.preferences.rulerUnits = Units.PIXELS;
app.displayDialogs = DialogModes.NO;
var problems = [];
try {
    var root = File($.fileName).parent.fsName.replace(/\\/g, '/');
    for (var s = 0; s < SCHEMES.length; s++)
        for (var i = 0; i < IMAGES.length; i++) {
            var problem = buildImage(root, SCHEMES[s], IMAGES[i]);
            if (problem) problems.push(problem);
        }
} finally {
    app.preferences.rulerUnits = units;
    app.displayDialogs = dialogs;
}
problems.length ? problems.join('\n') : 'ok';
