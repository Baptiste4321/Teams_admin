import AppKit
import CoreGraphics

let size: CGFloat = 1024
let colorSpace = CGColorSpaceCreateDeviceRGB()
guard let context = CGContext(
    data: nil,
    width: Int(size),
    height: Int(size),
    bitsPerComponent: 8,
    bytesPerRow: 0,
    space: colorSpace,
    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
) else {
    fatalError("Impossible de créer le contexte CG")
}

let nsContext = NSGraphicsContext(cgContext: context, flipped: false)
NSGraphicsContext.current = nsContext

// 1. Fond transparent
context.clear(CGRect(x: 0, y: 0, width: size, height: size))

// 2. Ombre portée sous le squircle
context.saveGState()
let shadowOffset = CGSize(width: 0, height: -28)
let shadowBlur: CGFloat = 48
context.setShadow(offset: shadowOffset, blur: shadowBlur, color: NSColor.black.withAlphaComponent(0.45).cgColor)

// 3. Squircle macOS standard (insetLayout: 104pt padding pour marge d'icône macOS)
let iconRect = CGRect(x: 104, y: 104, width: 816, height: 816)
let squirclePath = NSBezierPath(roundedRect: iconRect, xRadius: 184, yRadius: 184)

// Remplissage avec dégradé sombre Studio
let gradientBg = NSGradient(colors: [
    NSColor(red: 0.12, green: 0.14, blue: 0.22, alpha: 1.0), // Bleu nuit foncé
    NSColor(red: 0.07, green: 0.08, blue: 0.14, alpha: 1.0),
    NSColor(red: 0.04, green: 0.04, blue: 0.08, alpha: 1.0)
], atLocations: [0.0, 0.6, 1.0], colorSpace: .deviceRGB)!
gradientBg.draw(in: squirclePath, angle: -45)
context.restoreGState()

// 4. Bordure fine lumineuse (Inner Highlight)
context.saveGState()
let strokePath = NSBezierPath(roundedRect: iconRect.insetBy(dx: 1.5, dy: 1.5), xRadius: 182, yRadius: 182)
strokePath.lineWidth = 3
let borderGradient = NSGradient(colors: [
    NSColor.white.withAlphaComponent(0.35),
    NSColor(red: 0.38, green: 0.44, blue: 0.98, alpha: 0.2),
    NSColor.white.withAlphaComponent(0.05)
], atLocations: [0.0, 0.4, 1.0], colorSpace: .deviceRGB)!
context.saveGState()
strokePath.addClip()
borderGradient.draw(in: iconRect, angle: -45)
context.restoreGState()
context.restoreGState()

// 5. Grille de studio subtile / cercles concentriques radar
context.saveGState()
context.setStrokeColor(NSColor(red: 0.35, green: 0.45, blue: 0.85, alpha: 0.08).cgColor)
context.setLineWidth(2)
let center = CGPoint(x: size / 2, y: size / 2 + 10)
for radius in stride(from: 140, through: 320, by: 60) {
    context.strokeEllipse(in: CGRect(x: center.x - CGFloat(radius), y: center.y - CGFloat(radius), width: CGFloat(radius * 2), height: CGFloat(radius * 2)))
}
context.restoreGState()

// 6. Caméra Stylisée (Symbole vidéo haute définition)
let camRect = CGRect(x: 290, y: 360, width: 310, height: 230)
let camPath = NSBezierPath(roundedRect: camRect, xRadius: 44, yRadius: 44)

// Dégradé caméra : Indigo vers Cyan vibrant
let camGradient = NSGradient(colors: [
    NSColor(red: 0.32, green: 0.42, blue: 0.98, alpha: 1.0),
    NSColor(red: 0.22, green: 0.65, blue: 0.96, alpha: 1.0)
], atLocations: [0.0, 1.0], colorSpace: .deviceRGB)!

context.saveGState()
context.setShadow(offset: CGSize(width: 0, height: -12), blur: 32, color: NSColor(red: 0.2, green: 0.4, blue: 0.95, alpha: 0.5).cgColor)
camGradient.draw(in: camPath, angle: -30)
context.restoreGState()

// Objectif / cône de caméra (trapèze à droite)
let lensPath = NSBezierPath()
lensPath.move(to: CGPoint(x: 615, y: 440))
lensPath.line(to: CGPoint(x: 710, y: 390))
lensPath.curve(to: CGPoint(x: 724, y: 400), controlPoint1: CGPoint(x: 720, y: 390), controlPoint2: CGPoint(x: 724, y: 395))
lensPath.line(to: CGPoint(x: 724, y: 550))
lensPath.curve(to: CGPoint(x: 710, y: 560), controlPoint1: CGPoint(x: 724, y: 555), controlPoint2: CGPoint(x: 720, y: 560))
lensPath.line(to: CGPoint(x: 615, y: 510))
lensPath.close()

context.saveGState()
context.setShadow(offset: CGSize(width: 0, height: -10), blur: 28, color: NSColor(red: 0.2, green: 0.4, blue: 0.95, alpha: 0.4).cgColor)
camGradient.draw(in: lensPath, angle: -30)
context.restoreGState()

// Cercle optique intérieur de la caméra (lentille)
let lensCenter = CGPoint(x: 445, y: 475)
let lensOuter = CGRect(x: lensCenter.x - 62, y: lensCenter.y - 62, width: 124, height: 124)
let lensInner = CGRect(x: lensCenter.x - 38, y: lensCenter.y - 38, width: 76, height: 76)

let lensBg = NSColor(red: 0.08, green: 0.1, blue: 0.18, alpha: 0.9)
lensBg.setFill()
NSBezierPath(ovalIn: lensOuter).fill()

let lensApertureGradient = NSGradient(colors: [
    NSColor(red: 0.15, green: 0.2, blue: 0.35, alpha: 1.0),
    NSColor(red: 0.05, green: 0.08, blue: 0.15, alpha: 1.0)
])!
lensApertureGradient.draw(in: NSBezierPath(ovalIn: lensInner), angle: -45)

// Reflet sur la lentille
let reflexPath = NSBezierPath(ovalIn: CGRect(x: lensCenter.x - 22, y: lensCenter.y + 6, width: 28, height: 18))
NSColor.white.withAlphaComponent(0.45).setFill()
reflexPath.fill()

// 7. Ondes Sonores (Audio Waveform Studio) sous la caméra
let waveY: CGFloat = 265
let barWidth: CGFloat = 14
let barSpacing: CGFloat = 12
let heights: [CGFloat] = [28, 48, 80, 115, 65, 95, 120, 85, 55, 30]
let totalWaveWidth = CGFloat(heights.count) * barWidth + CGFloat(heights.count - 1) * barSpacing
let startX = (size - totalWaveWidth) / 2

for (i, h) in heights.enumerated() {
    let x = startX + CGFloat(i) * (barWidth + barSpacing)
    let y = waveY - h / 2
    let barRect = CGRect(x: x, y: y, width: barWidth, height: h)
    let barPath = NSBezierPath(roundedRect: barRect, xRadius: barWidth / 2, yRadius: barWidth / 2)
    
    // Gradient vert fluo -> émeraude pour audio capturé
    let waveGradient = NSGradient(colors: [
        NSColor(red: 0.2, green: 0.95, blue: 0.65, alpha: 0.95),
        NSColor(red: 0.1, green: 0.75, blue: 0.85, alpha: 0.8)
    ])!
    
    context.saveGState()
    context.setShadow(offset: .zero, blur: 12, color: NSColor(red: 0.2, green: 0.95, blue: 0.65, alpha: 0.4).cgColor)
    waveGradient.draw(in: barPath, angle: 90)
    context.restoreGState()
}

// 8. Bouton/Pastille "REC" Rouge Fluo Lumineuse en haut à droite
let recCenter = CGPoint(x: 690, y: 690)
let recGlowRadius: CGFloat = 46
let recDotRadius: CGFloat = 26

// Halo de lueur rouge
context.saveGState()
let redGlowColors = [
    NSColor.red.withAlphaComponent(0.6).cgColor,
    NSColor.red.withAlphaComponent(0.0).cgColor
] as CFArray
if let glowGrad = CGGradient(colorsSpace: colorSpace, colors: redGlowColors, locations: [0.0, 1.0]) {
    context.drawRadialGradient(glowGrad, startCenter: recCenter, startRadius: 0, endCenter: recCenter, endRadius: recGlowRadius, options: [])
}
context.restoreGState()

// Point REC rouge vibrant
let recPath = NSBezierPath(ovalIn: CGRect(x: recCenter.x - recDotRadius, y: recCenter.y - recDotRadius, width: recDotRadius * 2, height: recDotRadius * 2))
let recGradient = NSGradient(colors: [
    NSColor(red: 1.0, green: 0.35, blue: 0.35, alpha: 1.0),
    NSColor(red: 0.9, green: 0.05, blue: 0.1, alpha: 1.0)
])!
recGradient.draw(in: recPath, angle: -45)

// Reflet blanc sur point REC
let recReflex = NSBezierPath(ovalIn: CGRect(x: recCenter.x - 12, y: recCenter.y + 4, width: 12, height: 8))
NSColor.white.withAlphaComponent(0.7).setFill()
recReflex.fill()

// 9. Étoile IA / Intelligence Whisper en haut à gauche
let starCenter = CGPoint(x: 320, y: 690)
let starPath = NSBezierPath()
let starSize: CGFloat = 28
starPath.move(to: CGPoint(x: starCenter.x, y: starCenter.y + starSize))
starPath.curve(to: CGPoint(x: starCenter.x + starSize, y: starCenter.y), controlPoint1: CGPoint(x: starCenter.x, y: starCenter.y), controlPoint2: CGPoint(x: starCenter.x, y: starCenter.y))
starPath.curve(to: CGPoint(x: starCenter.x, y: starCenter.y - starSize), controlPoint1: CGPoint(x: starCenter.x, y: starCenter.y), controlPoint2: CGPoint(x: starCenter.x, y: starCenter.y))
starPath.curve(to: CGPoint(x: starCenter.x - starSize, y: starCenter.y), controlPoint1: CGPoint(x: starCenter.x, y: starCenter.y), controlPoint2: CGPoint(x: starCenter.x, y: starCenter.y))
starPath.curve(to: CGPoint(x: starCenter.x, y: starCenter.y + starSize), controlPoint1: CGPoint(x: starCenter.x, y: starCenter.y), controlPoint2: CGPoint(x: starCenter.x, y: starCenter.y))
starPath.close()

context.saveGState()
context.setShadow(offset: .zero, blur: 16, color: NSColor(red: 0.95, green: 0.75, blue: 0.3, alpha: 0.6).cgColor)
let starGradient = NSGradient(colors: [
    NSColor(red: 1.0, green: 0.9, blue: 0.45, alpha: 1.0),
    NSColor(red: 0.95, green: 0.65, blue: 0.2, alpha: 1.0)
])!
starGradient.draw(in: starPath, angle: -45)
context.restoreGState()

// 10. Export Image PNG 1024x1024
guard let cgImage = context.makeImage() else {
    fatalError("Impossible de générer le CGImage")
}

let bitmapRep = NSBitmapImageRep(cgImage: cgImage)
bitmapRep.size = NSSize(width: size, height: size)
guard let pngData = bitmapRep.representation(using: .png, properties: [:]) else {
    fatalError("Impossible d'encoder en PNG")
}

let outputPath = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "/tmp/MeetingCapture_1024.png"
let outputURL = URL(fileURLWithPath: outputPath)
try pngData.write(to: outputURL)
print("✅ Icône 1024x1024 générée avec succès dans : \(outputPath)")
