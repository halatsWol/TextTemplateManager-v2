// Builds the Store screenshots (2560x1440 PNG) from the PSD templates in templates\ and the window captures in
// captures\: per colour scheme and image, the caption and subline are typed into the text layers, the capture
// replaces the smart object's contents and is fitted into the frame, and the result goes to <scheme>\.
// Run in Photoshop: File > Scripts > Browse... (the English images below, both schemes).
// Scripted (design/store/Make-StoreScreenshots.ps1, through Photoshop's COM interface), with TTM_ARGS (or the
// script arguments) linesFile, capturesDir, outDir, scheme: one language's images, from a UTF-8 file with one line per image
// ("name | capture | caption | subline"), into outDir\<name>.png.
#target photoshop

var W = 2560;
// The frame a capture is fitted into (never enlarged), centred, bottom edge where the template's placeholder ends.
var BOX = { w: 2048, h: 1008, bottom: 1348 };
// Captions and sublines wider than this (a longer language) are set smaller until they fit.
var TEXT_MAX_W = 2240;
var SCHEMES = ['amber', 'slate'];
var IMAGES = [
    { name: '01-main', shot: 'store-main', caption: 'Your text templates, in one place', sub: 'Folders, rich text, tags and shortcuts for every reply you write again and again' },
    { name: '02-quick-paste', shot: 'store-qp-single', caption: 'Paste any template with a single key', sub: 'Shift+Alt+Y opens Quick Paste in any app' },
    { name: '03-multi-key', shot: 'store-qp-multi', caption: 'Or hold Alt and type a short code', sub: 'Multi-key shortcuts narrow the list as you type' },
    { name: '04-paste-modes', shot: 'store-pastemode', caption: 'Pasted in the format the app needs', sub: 'Auto, HTML/Jira, HTML, RTF, Markdown or plain text' },
    { name: '05-search', shot: 'store-search', caption: 'Find any template as you type', sub: 'By title, tags or the text itself, fuzzy or strict' },
    { name: '06-sync', shot: 'store-sync', caption: 'Share templates with your team', sub: 'A synced file on OneDrive or a network share' },
    { name: '07-backups', shot: 'store-backups', caption: 'Automatic backups you can restore', sub: 'After your changes, every two hours and daily' },
    { name: '08-tour', shot: 'store-tour', caption: 'A guided tour gets you started', sub: 'On sample templates; yours stay untouched' },
    { name: '09-translate', shot: 'store-translate', caption: 'Translate templates on your PC', sub: 'Offline in over 50 languages, formal German included (experimental)' }
];

function cid(id) { return charIDToTypeID(id); }
function sid(id) { return stringIDToTypeID(id); }

function replaceContents(file) {
    var d = new ActionDescriptor();
    d.putPath(cid('null'), file);
    executeAction(sid('placedLayerReplaceContents'), d, DialogModes.NO);
}

function px(v) { return v.as('px'); }
function trim(s) { return s.replace(/^\s+|\s+$/g, ''); }

function fitIntoBox(layer) {
    var b = layer.boundsNoEffects;
    var w = px(b[2]) - px(b[0]), h = px(b[3]) - px(b[1]);
    var scale = Math.min(BOX.w / w, BOX.h / h, 1);
    if (scale < 1) layer.resize(scale * 100, scale * 100, AnchorPosition.MIDDLECENTER);
    b = layer.boundsNoEffects;
    w = px(b[2]) - px(b[0]);
    layer.translate((W - w) / 2 - px(b[0]), BOX.bottom - px(b[3]));
}

function setText(layer, text) {
    layer.textItem.contents = text;
    for (var i = 0; i < 40; i++) {
        var b = layer.boundsNoEffects;
        if (px(b[2]) - px(b[0]) <= TEXT_MAX_W) return;
        layer.textItem.size = new UnitValue(layer.textItem.size.as('pt') * 0.96, 'pt');
    }
}

function buildImage(root, scheme, image, capturesDir, outFile) {
    var capture = new File(capturesDir + '/' + image.shot + '.png');
    if (!capture.exists) return 'missing ' + capture.fsName;
    var doc = app.open(new File(root + '/templates/store-template-' + scheme + '.psd'));
    setText(doc.layers.getByName('Caption'), image.caption);
    setText(doc.layers.getByName('Subline'), image.sub);
    var shot = doc.layers.getByName('Screenshot (Replace Contents)');
    doc.activeLayer = shot;
    replaceContents(capture);
    fitIntoBox(doc.activeLayer);

    var folder = outFile.parent;
    if (!folder.exists) folder.create();
    var options = new PNGSaveOptions();
    options.compression = 9;
    doc.saveAs(outFile, options, true, Extension.LOWERCASE);
    doc.close(SaveOptions.DONOTSAVECHANGES);
    return null;
}

var units = app.preferences.rulerUnits, dialogs = app.displayDialogs;
app.preferences.rulerUnits = Units.PIXELS;
app.displayDialogs = DialogModes.NO;
var problems = [];
try {
    var root = File($.fileName).parent.fsName.replace(/\\/g, '/');
    // Plain ifs: ExtendScript misreads a nested conditional expression here.
    var args = null;
    if (typeof TTM_ARGS !== 'undefined') args = TTM_ARGS;
    else if (typeof arguments !== 'undefined' && arguments.length >= 4) args = arguments;
    if (args) {
        var lines = new File(args[0]);
        lines.encoding = 'UTF-8';
        lines.open('r');
        var rows = lines.read().replace(/\r/g, '').split('\n');
        lines.close();
        for (var r = 0; r < rows.length; r++) {
            var f = rows[r].split('|');
            if (f.length < 4) continue;
            var image = { name: trim(f[0]), shot: trim(f[1]), caption: trim(f[2]), sub: trim(f.slice(3).join('|')) };
            var problem = buildImage(root, args[3], image, args[1], new File(args[2] + '/' + image.name + '.png'));
            if (problem) problems.push(problem);
        }
    } else {
        for (var s = 0; s < SCHEMES.length; s++)
            for (var i = 0; i < IMAGES.length; i++) {
                var problem = buildImage(root, SCHEMES[s], IMAGES[i], root + '/captures/en-US',
                    new File(root + '/' + SCHEMES[s] + '/' + IMAGES[i].name + '-' + SCHEMES[s] + '.png'));
                if (problem) problems.push(problem);
            }
    }
} catch (e) {
    problems.push('line ' + e.line + ': ' + e.message);
} finally {
    app.preferences.rulerUnits = units;
    // Started over COM, displayDialogs reads as a value it won't take back.
    try { app.displayDialogs = dialogs; } catch (ignored) { }
}
problems.length ? problems.join('\n') : 'ok';
