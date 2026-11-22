
import Foundation
import AppKit

// MARK: - Saved Portal State

struct PortalSavedState: Codable {
    var id: UUID
    var title: String
    var x: Double
    var y: Double
    var width: Double
    var height: Double
    var red: Double
    var green: Double
    var blue: Double
    var opacity: Double
    var shortcutPaths: [String]


    init(
        id: UUID = UUID(),
        title: String,
        x: Double,
        y: Double,
        width: Double,
        height: Double,
        red: Double,
        green: Double,
        blue: Double,
        opacity: Double,
        shortcutPaths: [String]
    ) {
        self.id = id
        self.title = title
        self.x = x
        self.y = y
        self.width = width
        self.height = height
        self.red = red
        self.green = green
        self.blue = blue
        self.opacity = opacity
        self.shortcutPaths = shortcutPaths
    }


    // Supports migration from the original single-Portal JSON file.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)

        id = try container.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        title = try container.decode(String.self, forKey: .title)
        x = try container.decode(Double.self, forKey: .x)
        y = try container.decode(Double.self, forKey: .y)
        width = try container.decode(Double.self, forKey: .width)
        height = try container.decode(Double.self, forKey: .height)
        red = try container.decode(Double.self, forKey: .red)
        green = try container.decode(Double.self, forKey: .green)
        blue = try container.decode(Double.self, forKey: .blue)
        opacity = try container.decode(Double.self, forKey: .opacity)
        shortcutPaths = try container.decode([String].self, forKey: .shortcutPaths)
    }

    private enum CodingKeys: String, CodingKey {
        case id, title, x, y, width, height
        case red, green, blue, opacity, shortcutPaths
    }
}

// MARK: - Central Portal Storage

final class PortalStore {

    private static var storageURL: URL {
        let applicationSupport = FileManager.default.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        )[0]

        return applicationSupport
            .appendingPathComponent("Portals")
            .appendingPathComponent("Portals.json")
    }

    // Load all saved Portals, migrating the old single-Portal format if necessary.
    static func loadAll() -> [PortalSavedState] {

        guard FileManager.default.fileExists(atPath: storageURL.path) else {
            print("No saved Portals found")
            return []
        }

        do {
            let data = try Data(contentsOf: storageURL)

            // Try the new multiple-Portal format first.
            if let states = try? JSONDecoder().decode(
                [PortalSavedState].self,
                from: data
            ) {
                print("Loaded \(states.count) Portals")
                return states
            }

            // Fall back to the original single-Portal format.
            let oldState = try JSONDecoder().decode(
                PortalSavedState.self,
                from: data
            )

            print("Migrating existing single Portal")
            saveAll([oldState])

            return [oldState]

        } catch {
            print("Could not load Portal data: \(error)")
            return []
        }
    }

    // Save the complete collection.
    static func saveAll(_ states: [PortalSavedState]) {

        do {
            let folder = storageURL.deletingLastPathComponent()

            try FileManager.default.createDirectory(
                at: folder,
                withIntermediateDirectories: true
            )

            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]

            let data = try encoder.encode(states)

            try data.write(to: storageURL, options: .atomic)

            print("Saved \(states.count) Portals")

        } catch {
            print("Could not save Portals: \(error)")
        }
    }

    // Add a Portal or update its existing entry.
    static func update(_ state: PortalSavedState) {

        var states = loadAll()

        if let index = states.firstIndex(where: { $0.id == state.id }) {
            states[index] = state
        } else {
            states.append(state)
        }

        saveAll(states)
    }

    // Remove a Portal from the saved collection.
    static func remove(id: UUID) {

        var states = loadAll()
        states.removeAll { $0.id == id }
        saveAll(states)
    }
}
