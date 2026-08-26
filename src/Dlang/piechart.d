module piechart;

import std.stdio;
import std.json;
import std.file;
import std.math;
import std.conv;
import std.algorithm;
import arsd.color;
import arsd.png;
import font8x8;

string removeAccents(string s) {
    char[] result;
    foreach(dchar c; s) {
        switch(c) {
            case 'á', 'à', 'ä', 'â': result ~= 'a'; break;
            case 'Á', 'À', 'Ä', 'Â': result ~= 'A'; break;
            case 'é', 'è', 'ë', 'ê': result ~= 'e'; break;
            case 'É', 'È', 'Ë', 'Ê': result ~= 'E'; break;
            case 'í', 'ì', 'ï', 'î': result ~= 'i'; break;
            case 'Í', 'Ì', 'Ï', 'Î': result ~= 'I'; break;
            case 'ó', 'ò', 'ö', 'ô': result ~= 'o'; break;
            case 'Ó', 'Ò', 'Ö', 'Ô': result ~= 'O'; break;
            case 'ú', 'ù', 'ü', 'û': result ~= 'u'; break;
            case 'Ú', 'Ù', 'Ü', 'Û': result ~= 'U'; break;
            case 'ñ': result ~= 'n'; break;
            case 'Ñ': result ~= 'N'; break;
            case '¿', '¡': break;
            default:
                if (c <= 127) result ~= cast(char)c;
                else result ~= '?';
                break;
        }
    }
    return result.idup;
}

void drawString(TrueColorImage img, string text, int x, int y, Color color, int scale = 2) {
    string cleanText = removeAccents(text);
    int curX = x;
    foreach(char c; cleanText) {
        if (c > 127) c = '?';
        ubyte[8] glyph = font8x8_basic[c];
        for (int row = 0; row < 8; row++) {
            for (int col = 0; col < 8; col++) {
                if ((glyph[row] >> col) & 1) {
                    for(int sy=0; sy<scale; sy++) {
                        for(int sx=0; sx<scale; sx++) {
                            int px = curX + col * scale + sx;
                            int py = y + row * scale + sy;
                            if (px >= 0 && px < img.width && py >= 0 && py < img.height) {
                                img.imageData.colors[py * img.width + px] = color;
                            }
                        }
                    }
                }
            }
        }
        curX += 8 * scale;
    }
}

void drawFilledRect(TrueColorImage img, int x, int y, int w, int h, Color color) {
    for (int r = y; r < y + h; r++) {
        for (int c = x; c < x + w; c++) {
            if (c >= 0 && c < img.width && r >= 0 && r < img.height) {
                img.imageData.colors[r * img.width + c] = color;
            }
        }
    }
}

Color[] getPalette() {
    return [
        Color(231, 76, 60),
        Color(52, 152, 219),
        Color(46, 204, 113),
        Color(241, 196, 15),
        Color(155, 89, 182),
        Color(230, 126, 34),
        Color(26, 188, 156),
        Color(236, 240, 241),
        Color(149, 165, 166),
        Color(52, 73, 94)
    ];
}

void main(string[] args) {
    if (args.length < 2) {
        writeln("Usage: piechart <output.png>");
        return;
    }
    string outFile = args[1];
    
    string input = readText("/dev/stdin");
    JSONValue j = parseJSON(input);
    
    auto labelsJson = j["labels"].array;
    auto valuesJson = j["values"].array;
    
    string[] labels;
    double[] values;
    double total = 0;
    
    foreach(val; labelsJson) labels ~= val.str;
    foreach(val; valuesJson) {
        double v = val.type == JSONType.integer ? val.integer : val.floating;
        values ~= v;
        total += v;
    }
    
    int width = 800;
    int pieHeight = 450;
    int legendHeight = cast(int)(labels.length * 40 + 40);
    int height = pieHeight + legendHeight;
    
    auto img = new TrueColorImage(width, height);
    drawFilledRect(img, 0, 0, width, height, Color(43, 45, 49));
    
    int cx = width / 2;
    int cy = pieHeight / 2 + 20;
    int radius = 180;
    
    auto palette = getPalette();
    
    double[] endAngles;
    double currentAcc = 0;
    foreach(v; values) {
        currentAcc += v;
        endAngles ~= total > 0 ? (currentAcc / total) * 2 * PI : 0;
    }
    
    if (total > 0) {
        for (int y = cy - radius; y <= cy + radius; y++) {
            for (int x = cx - radius; x <= cx + radius; x++) {
                double dx = x - cx;
                double dy = y - cy;
                double dist = sqrt(dx*dx + dy*dy);
                if (dist <= radius) {
                    double angle = atan2(dy, dx) + PI/2.0;
                    if (angle < 0) angle += 2 * PI;
                    
                    for (int i = 0; i < endAngles.length; i++) {
                        if (angle <= endAngles[i]) {
                            img.imageData.colors[y * img.width + x] = palette[i % palette.length];
                            break;
                        }
                    }
                }
            }
        }
    } else {
        drawString(img, "Sin votos", cx - 70, cy, Color(255, 255, 255), 3);
    }
    
    int legendY = pieHeight;
    for(int i = 0; i < labels.length; i++) {
        Color c = palette[i % palette.length];
        int ly = legendY + i * 40;
        drawFilledRect(img, cx - 150, ly, 20, 20, c);
        
        string lbl = labels[i];
        if (lbl.length > 35) lbl = lbl[0..32] ~ "...";
        
        double percentage = total > 0 ? (values[i] / total) * 100.0 : 0;
        import std.format;
        string text = format("%s (%.1f%%)", lbl, percentage);
        drawString(img, text, cx - 110, ly + 2, Color(255, 255, 255), 2);
    }
    
    writePng(outFile, img);
}
