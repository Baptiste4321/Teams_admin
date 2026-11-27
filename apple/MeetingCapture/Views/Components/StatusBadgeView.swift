import SwiftUI

/// Badge indicateur de flux
public struct StatusBadgeView: View {
    public var icon: String
    public var text: String
    public var color: Color
    
    public init(icon: String, text: String, color: Color) {
        self.icon = icon
        self.text = text
        self.color = color
    }
    
    public var body: some View {
        HStack(spacing: 4) {
            Text(icon)
                .font(.system(size: 10))
            Text(text)
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(color)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(Color(white: 0.12))
        .cornerRadius(6)
        .overlay(
            RoundedRectangle(cornerRadius: 6)
                .stroke(Color(white: 0.18), lineWidth: 1)
        )
    }
}
