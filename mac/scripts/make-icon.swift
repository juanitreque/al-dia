// Genera packaging/AppIcon.icns. Uso: swift scripts/make-icon.swift
import AppKit

func render(_ lado: CGFloat) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(lado), pixelsHigh: Int(lado), bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let m = lado * 0.1
    let rect = NSRect(x: m, y: m, width: lado - 2 * m, height: lado - 2 * m)
    let forma = NSBezierPath(roundedRect: rect, xRadius: rect.width * 0.225, yRadius: rect.width * 0.225)
    NSGradient(colors: [NSColor(red: 0.10, green: 0.55, blue: 0.45, alpha: 1),
                        NSColor(red: 0.04, green: 0.33, blue: 0.30, alpha: 1)])!.draw(in: forma, angle: -90)
    let config = NSImage.SymbolConfiguration(pointSize: lado * 0.42, weight: .semibold)
        .applying(.init(paletteColors: [.white]))
    if let s = NSImage(systemSymbolName: "eurosign", accessibilityDescription: nil)?.withSymbolConfiguration(config) {
        let r = NSRect(x: (lado - s.size.width) / 2, y: (lado - s.size.height) / 2, width: s.size.width, height: s.size.height)
        s.draw(in: r)
    }
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

let set = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("AppIcon.iconset")
try? FileManager.default.removeItem(at: set)
try! FileManager.default.createDirectory(at: set, withIntermediateDirectories: true)
for base in [16, 32, 128, 256, 512] {
    try! render(CGFloat(base)).write(to: set.appendingPathComponent("icon_\(base)x\(base).png"))
    try! render(CGFloat(base * 2)).write(to: set.appendingPathComponent("icon_\(base)x\(base)@2x.png"))
}
let p = Process()
p.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
p.arguments = ["-c", "icns", set.path, "-o", "packaging/AppIcon.icns"]
try! p.run(); p.waitUntilExit()
print(p.terminationStatus == 0 ? "✓ packaging/AppIcon.icns" : "iconutil falló")
