import SwiftUI

/// Composant VU-Mètre réactif
public struct VuMeterView: View {
    public var label: String
    public var level: Float
    public var color: Color
    
    public init(label: String, level: Float, color: Color) {
        self.label = label
        self.level = level
        self.color = color
    }
    
    public var body: some View {
        HStack(spacing: 6) {
            Text(label)
                .font(.system(size: 9, weight: .bold))
                .foregroundColor(.secondary)
            
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 2)
                        .fill(Color(white: 0.15))
                    
                    RoundedRectangle(cornerRadius: 2)
                        .fill(color)
                        .frame(width: max(0, min(geo.size.width, geo.size.width * CGFloat(level))))
                        .animation(.linear(duration: 0.1), value: level)
                }
            }
            .frame(height: 5)
        }
    }
}
