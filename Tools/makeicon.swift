// Tools/makeicon.swift — run with: swift Tools/makeicon.swift
// Draws Resources/AppIcon-1024.png (a usage ring on a rounded dark tile).
import AppKit

let size: CGFloat = 1024
let image = NSImage(size: NSSize(width: size, height: size))
image.lockFocus()

NSColor(calibratedRed: 0.12, green: 0.12, blue: 0.16, alpha: 1).setFill()
NSBezierPath(roundedRect: NSRect(x: 0, y: 0, width: size, height: size),
             xRadius: 180, yRadius: 180).fill()

let center = NSPoint(x: size / 2, y: size / 2)
let radius: CGFloat = 280
let track = NSBezierPath()
track.appendArc(withCenter: center, radius: radius, startAngle: 0, endAngle: 360)
track.lineWidth = 110
NSColor(white: 1, alpha: 0.16).setStroke()
track.stroke()

let progress = NSBezierPath()
progress.appendArc(withCenter: center, radius: radius, startAngle: 90, endAngle: 90 - 360 * 0.62, clockwise: true)
progress.lineWidth = 110
progress.lineCapStyle = .round
NSColor(calibratedRed: 0.85, green: 0.47, blue: 0.34, alpha: 1).setStroke()
progress.stroke()

image.unlockFocus()

guard let tiff = image.tiffRepresentation,
      let rep = NSBitmapImageRep(data: tiff),
      let png = rep.representation(using: .png, properties: [:]) else {
    FileHandle.standardError.write(Data("icon render failed\n".utf8))
    exit(1)
}
try! FileManager.default.createDirectory(atPath: "Resources", withIntermediateDirectories: true)
try! png.write(to: URL(fileURLWithPath: "Resources/AppIcon-1024.png"))
print("wrote Resources/AppIcon-1024.png")
