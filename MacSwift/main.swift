import AppKit
import CoreGraphics


// MARK: - Shared Notifications

extension Notification.Name {
    static let portalContentResized = Notification.Name("portalContentResized")
}

// MARK: - Shortcut Click Handler

class ShortcutClickHandler: NSObject {
    let url: URL

    init(url: URL) {
        self.url = url
    }

    @objc func openShortcut(_ sender: Any?) {
        NSWorkspace.shared.open(url)
    }
}

// MARK: - Draggable Shortcut Button

class ShortcutButton: NSButton {
    var onDragEnded: ((ShortcutButton, NSPoint, NSPoint) -> Void)?
    var onRemove: ((ShortcutButton) -> Void)?
    var shortcutURL: URL?

    override func rightMouseDown(with event: NSEvent) {
        let menu = NSMenu()
        let removeItem = NSMenuItem(
            title: "Remove from Portal",
            action: #selector(removeFromPortal(_:)),
            keyEquivalent: ""
        )
        removeItem.target = self
        menu.addItem(removeItem)
        menu.popUp(positioning: nil, at: event.locationInWindow, in: self)
    }

    @objc func removeFromPortal(_ sender: Any?) {
        onRemove?(self)
    }

    override func mouseDown(with event: NSEvent) {
        guard let parent = superview else { return }

        let initialPoint = parent.convert(event.locationInWindow, from: nil)
        let offset = NSPoint(
            x: initialPoint.x - frame.origin.x,
            y: initialPoint.y - frame.origin.y
        )

        var didDrag = false
        var finished = false
        var releasePoint = initialPoint

        while !finished {
            guard let nextEvent = NSApp.nextEvent(
                matching: [.leftMouseDragged, .leftMouseUp],
                until: .distantFuture,
                inMode: .eventTracking,
                dequeue: true
            ) else { break }

            let currentPoint = parent.convert(nextEvent.locationInWindow, from: nil)

            if nextEvent.type == .leftMouseDragged {
                let distance = hypot(
                    currentPoint.x - initialPoint.x,
                    currentPoint.y - initialPoint.y
                )
                if distance > 5 { didDrag = true }

                if didDrag {
                    var newFrame = frame
                    newFrame.origin = NSPoint(
                        x: currentPoint.x - offset.x,
                        y: currentPoint.y - offset.y
                    )
                    frame = newFrame
                }
            } else if nextEvent.type == .leftMouseUp {
                releasePoint = currentPoint
                finished = true
            }
        }

        if didDrag {
            let windowPoint = parent.convert(releasePoint, to: nil)
            let screenPoint = parent.window?.convertPoint(toScreen: windowPoint) ?? .zero
            onDragEnded?(self, releasePoint, screenPoint)
        } else {
            performClick(nil)
        }
    }
}

// MARK: - Portal Context Menu Controller

class PortalMenuController: NSObject {
   weak var portalWindow: NSWindow?
var onSave: (() -> Void)?
var onDelete: (() -> Void)?

    private var currentColor = NSColor(
        calibratedRed: 0.15,
        green: 0.17,
        blue: 0.21,
        alpha: 0.88
    )
    private var currentOpacity: CGFloat = 0.88

    init(window: NSWindow) {
        portalWindow = window
    }

    func showMenu(at point: NSPoint, in view: NSView) {
        let menu = NSMenu()

        let colourItem = NSMenuItem(
            title: "Change Portal Colour",
            action: nil,
            keyEquivalent: ""
        )
        let colourMenu = NSMenu()
        let colours: [(String, NSColor)] = [
            ("Dark Grey", .darkGray),
            ("Blue", .systemBlue),
            ("Green", .systemGreen),
            ("Purple", .systemPurple),
            ("Red", .systemRed),
            ("Brown", .brown)
        ]

        for (name, colour) in colours {
            let item = NSMenuItem(
                title: name,
                action: #selector(changePortalColour(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = colour
            colourMenu.addItem(item)
        }
        menu.setSubmenu(colourMenu, for: colourItem)
        menu.addItem(colourItem)

        let transparencyItem = NSMenuItem(
            title: "Change Transparency",
            action: nil,
            keyEquivalent: ""
        )
        let transparencyMenu = NSMenu()

        for percentage in [20, 40, 60, 80, 100] {
            let item = NSMenuItem(
                title: "\(percentage)%",
                action: #selector(changeTransparency(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = NSNumber(value: Double(percentage) / 100.0)
            transparencyMenu.addItem(item)
        }
        menu.setSubmenu(transparencyMenu, for: transparencyItem)
        menu.addItem(transparencyItem)

        menu.addItem(NSMenuItem.separator())

        let deleteItem = NSMenuItem(
            title: "Delete Portal",
            action: #selector(deletePortal(_:)),
            keyEquivalent: ""
        )
        deleteItem.target = self
        menu.addItem(deleteItem)

        menu.popUp(positioning: nil, at: point, in: view)
    }

    @objc func changePortalColour(_ sender: NSMenuItem) {
        guard let colour = sender.representedObject as? NSColor,
              let window = portalWindow else { return }

        currentColor = colour
        window.backgroundColor = colour.withAlphaComponent(currentOpacity)
        onSave?()
    }

    @objc func changeTransparency(_ sender: NSMenuItem) {
        guard let number = sender.representedObject as? NSNumber,
              let window = portalWindow else { return }

        currentOpacity = CGFloat(number.doubleValue)
        window.backgroundColor = currentColor.withAlphaComponent(currentOpacity)
        onSave?()
    }

    @objc func deletePortal(_ sender: Any?) {
    guard let window = portalWindow else { return }

    let alert = NSAlert()
    alert.messageText = "Delete this Portal?"
    alert.informativeText = "The Portal will close. Original files and folders will not be deleted."
    alert.alertStyle = .warning
    alert.addButton(withTitle: "Delete")
    alert.addButton(withTitle: "Cancel")

    if alert.runModal() == .alertFirstButtonReturn {
        onDelete?()
        window.close()
    }
}

    func appearanceComponents() -> (Double, Double, Double, Double) {
        let colour = currentColor.usingColorSpace(.deviceRGB) ?? NSColor.darkGray
        var red: CGFloat = 0
        var green: CGFloat = 0
        var blue: CGFloat = 0
        var alpha: CGFloat = 0
        colour.getRed(&red, green: &green, blue: &blue, alpha: &alpha)

        return (Double(red), Double(green), Double(blue), Double(currentOpacity))
    }

    func restoreAppearance(red: Double, green: Double, blue: Double, opacity: Double) {
        currentColor = NSColor(
            calibratedRed: red,
            green: green,
            blue: blue,
            alpha: 1
        )
        currentOpacity = CGFloat(opacity)
        portalWindow?.backgroundColor = currentColor.withAlphaComponent(currentOpacity)
    }
}

// MARK: - Portal Window and Content View

class PortalWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

class PortalContentView: NSView {
    var onDrop: (([URL]) -> Void)?
    var onContextMenu: ((NSPoint) -> Void)?

    override func rightMouseDown(with event: NSEvent) {
        onContextMenu?(event.locationInWindow)
    }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        registerForDraggedTypes([.fileURL])
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        print("DRAG ENTERED PORTAL")
        return .copy
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        return .copy
    }

    override func prepareForDragOperation(_ sender: NSDraggingInfo) -> Bool {
        return true
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let urls = sender.draggingPasteboard.readObjects(
            forClasses: [NSURL.self],
            options: [.urlReadingFileURLsOnly: true]
        ) as? [URL] ?? []

        guard !urls.isEmpty else { return false }
        var acceptedAliases: [URL] = []

        for url in urls {
            do {
                let values = try url.resourceValues(forKeys: [.isAliasFileKey])
                if values.isAliasFile == true {
                    acceptedAliases.append(url)
                    print("Accepted alias: \(url.lastPathComponent)")
                } else {
                    print("Rejected non-alias: \(url.lastPathComponent)")
                }
            } catch {
                print("Could not identify item: \(url.lastPathComponent)")
            }
        }

        guard !acceptedAliases.isEmpty else {
            print("No valid aliases dropped into Portal")
            return false
        }

        onDrop?(acceptedAliases)
        return true
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        DispatchQueue.main.async {
            NotificationCenter.default.post(
                name: .portalContentResized,
                object: self
            )
        }
    }
}

// MARK: - Editable Portal Title

class PortalTitleField: NSTextField {
    var originalTitleWhenEditing: String?

    override func mouseDown(with event: NSEvent) {
        guard event.clickCount >= 2 else {
            super.mouseDown(with: event)
            return
        }

        originalTitleWhenEditing = stringValue
        isEditable = true
        isSelectable = true
        isBordered = false
        drawsBackground = false

        window?.makeFirstResponder(self)
        window?.fieldEditor(false, for: self)?.selectAll(nil)
    }
}

// MARK: - Portal Controller

class PortalController: NSObject, NSTextFieldDelegate {
    let portalID: UUID
    let window: PortalWindow
    let contentView: PortalContentView
    let menuController: PortalMenuController
    let titleField: PortalTitleField

    private var shortcutButtons: [ShortcutButton] = []
    private var shortcutHandlers: [ShortcutClickHandler] = []
    private var observers: [NSObjectProtocol] = []
    private var isRestoringState = false

    private let defaultBackground = NSColor(
        calibratedRed: 0.15,
        green: 0.17,
        blue: 0.21,
        alpha: 0.88
    )

   init(
    frame: NSRect,
    title: String = "Programming",
    restoreSavedState: Bool = true,
    portalID: UUID = UUID()
) {
        let portalWindow = PortalWindow(
            contentRect: frame,
            styleMask: [.borderless, .resizable],
            backing: .buffered,
            defer: false
        )
        window = portalWindow

        self.portalID = portalID

        let portalContentView = PortalContentView(
            frame: NSRect(origin: .zero, size: frame.size)
        )
        contentView = portalContentView

        let portalTitle = PortalTitleField(frame: .zero)
        portalTitle.stringValue = title
        titleField = portalTitle

        menuController = PortalMenuController(window: portalWindow)

super.init()

menuController.onDelete = { [weak self] in
    guard let self else { return }

    PortalStore.remove(id: self.portalID)
    print("Portal deleted from storage: \(self.portalID)")
}

configureWindow()
        configureContentView()
        configureTitle()
        configureCallbacks()
        installObservers()
        
    }

    deinit {
        for observer in observers {
            NotificationCenter.default.removeObserver(observer)
        }
    }

    private func configureWindow() {
        let desktopLevel = CGWindowLevelForKey(.desktopIconWindow)
        window.level = NSWindow.Level(rawValue: Int(desktopLevel) + 1)
        window.ignoresMouseEvents = false
        window.isOpaque = false
        window.backgroundColor = defaultBackground
        window.hasShadow = false
        window.isMovable = true
        window.isMovableByWindowBackground = true
        window.minSize = NSSize(width: 220, height: 160)
        window.maxSize = NSSize(width: 1000, height: 800)
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
    }

    private func configureContentView() {
        window.contentView = contentView
    }

    private func configureTitle() {
        titleField.frame = NSRect(
            x: 15,
            y: contentView.bounds.height - 40,
            width: max(100, contentView.bounds.width - 30),
            height: 25
        )
        titleField.font = NSFont.boldSystemFont(ofSize: 17)
        titleField.textColor = .white
        titleField.alignment = .center
        titleField.delegate = self
        titleField.isEditable = false
        titleField.isSelectable = false
        titleField.isBordered = false
        titleField.isBezeled = false
        titleField.drawsBackground = false
        titleField.backgroundColor = .clear
        titleField.focusRingType = .none
        contentView.addSubview(titleField)
    }

    // Enter commits the edited title; Escape restores the previous title.
    func control(_ control: NSControl, textView: NSTextView,
                 doCommandBy commandSelector: Selector) -> Bool {
        guard control === titleField else { return false }

        if commandSelector == #selector(NSResponder.insertNewline(_:)) {
            titleField.stringValue = textView.string
            finishTitleEditing(save: true)
            return true
        }

        if commandSelector == #selector(NSResponder.cancelOperation(_:)) {
            if let original = titleField.originalTitleWhenEditing {
                titleField.stringValue = original
            }
            finishTitleEditing(save: false)
            return true
        }

        return false
    }

    private func finishTitleEditing(save: Bool) {
        window.makeFirstResponder(contentView)
        titleField.isEditable = false
        titleField.isSelectable = false
        titleField.originalTitleWhenEditing = nil

        if save {
            savePortalState()
            print("Portal title saved: \(titleField.stringValue)")
        }
    }

    private func configureCallbacks() {
        menuController.onSave = { [weak self] in
            self?.savePortalState()
        }

        contentView.onContextMenu = { [weak self] point in
            guard let self else { return }
            self.menuController.showMenu(at: point, in: self.contentView)
        }

        contentView.onDrop = { [weak self] urls in
            guard let self else { return }
            for url in urls {
                guard let storedURL = Self.storeAliasAndRemoveOriginal(url) else {
                    print("Failed to add shortcut: \(url.lastPathComponent)")
                    continue
                }
                self.addShortcut(storedURL)
                print("Added shortcut: \(storedURL.lastPathComponent)")
            }
            self.savePortalState()
        }
    }

    private func installObservers() {
        observers.append(
            NotificationCenter.default.addObserver(
                forName: .portalContentResized,
                object: contentView,
                queue: .main
            ) { [weak self] _ in
                self?.updateTitleFrame()
                self?.arrangeShortcuts()
            }
        )

        observers.append(
            NotificationCenter.default.addObserver(
                forName: NSWindow.didMoveNotification,
                object: window,
                queue: .main
            ) { [weak self] _ in
                self?.savePortalState()
            }
        )

        observers.append(
            NotificationCenter.default.addObserver(
                forName: NSWindow.didResizeNotification,
                object: window,
                queue: .main
            ) { [weak self] _ in
                self?.updateTitleFrame()
                self?.savePortalState()
            }
        )
    }

    private func updateTitleFrame() {
        titleField.frame = NSRect(
            x: 15,
            y: contentView.bounds.height - 40,
            width: max(100, contentView.bounds.width - 30),
            height: 25
        )
    }

    // MARK: Persistence

    private var portalStorageURL: URL {
        let applicationSupport = FileManager.default.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        )[0]
        return applicationSupport
            .appendingPathComponent("Portals")
            .appendingPathComponent("Portals.json")
    }

    func savePortalState() {
        guard !isRestoringState else { return }

        let frame = window.frame
        let appearance = menuController.appearanceComponents()

        let state = PortalSavedState(
            id: portalID,
            title: titleField.stringValue,
            x: Double(frame.origin.x),
            y: Double(frame.origin.y),
            width: Double(frame.width),
            height: Double(frame.height),
            red: appearance.0,
            green: appearance.1,
            blue: appearance.2,
            opacity: appearance.3,
            shortcutPaths: shortcutButtons.compactMap { $0.shortcutURL?.path }
        )

        PortalStore.update(state)
    }

    
func restorePortalState(_ state: PortalSavedState) {

    isRestoringState = true
    defer { isRestoringState = false }

    window.setFrame(
        NSRect(
            x: state.x,
            y: state.y,
            width: state.width,
            height: state.height
        ),
        display: true
    )

    titleField.stringValue = state.title

    menuController.restoreAppearance(
        red: state.red,
        green: state.green,
        blue: state.blue,
        opacity: state.opacity
    )

    for path in state.shortcutPaths {
        guard FileManager.default.fileExists(atPath: path) else {
            print("Shortcut no longer exists: \(path)")
            continue
        }

        addShortcut(URL(fileURLWithPath: path))
    }

    arrangeShortcuts()
    print("Portal restored: \(state.title)")
}


    // MARK: Shortcut Layout and Actions

    private func arrangeShortcuts() {
        let iconWidth: CGFloat = 76
        let iconHeight: CGFloat = 80
        let horizontalSpacing: CGFloat = 6
        let verticalSpacing: CGFloat = 8
        let leftMargin: CGFloat = 12
        let rightMargin: CGFloat = 12
        let topMargin: CGFloat = 55

        let availableWidth = contentView.bounds.width - leftMargin - rightMargin
        let columns = max(
            1,
            Int((availableWidth + horizontalSpacing) / (iconWidth + horizontalSpacing))
        )

        for (index, button) in shortcutButtons.enumerated() {
            let column = index % columns
            let row = index / columns
            let x = leftMargin + CGFloat(column) * (iconWidth + horizontalSpacing)
            let y = contentView.bounds.height - topMargin - iconHeight
                - CGFloat(row) * (iconHeight + verticalSpacing)

            button.frame = NSRect(x: x, y: y, width: iconWidth, height: iconHeight)
        }
    }

    private func moveShortcut(_ button: ShortcutButton, to point: NSPoint) {
        guard let oldIndex = shortcutButtons.firstIndex(where: { $0 === button }) else {
            return
        }

        let iconWidth: CGFloat = 76
        let iconHeight: CGFloat = 80
        let horizontalSpacing: CGFloat = 6
        let verticalSpacing: CGFloat = 8
        let leftMargin: CGFloat = 12
        let topMargin: CGFloat = 55
        let availableWidth = contentView.bounds.width - 24

        let columns = max(
            1,
            Int((availableWidth + horizontalSpacing) / (iconWidth + horizontalSpacing))
        )

        let column = max(
            0,
            min(
                columns - 1,
                Int((point.x - leftMargin) / (iconWidth + horizontalSpacing))
            )
        )
        let row = max(
            0,
            Int((contentView.bounds.height - topMargin - point.y)
                / (iconHeight + verticalSpacing))
        )
        let targetIndex = min(shortcutButtons.count - 1, row * columns + column)

        shortcutButtons.remove(at: oldIndex)
        shortcutButtons.insert(button, at: targetIndex)
        arrangeShortcuts()
        savePortalState()
    }

    private func removeShortcut(_ button: ShortcutButton) {
        guard let index = shortcutButtons.firstIndex(where: { $0 === button }) else {
            return
        }

        print("Removing shortcut: \(button.title)")
        shortcutButtons.remove(at: index)
        button.removeFromSuperview()
        arrangeShortcuts()
        savePortalState()
    }

    private func addShortcut(_ url: URL) {
        let icon = NSWorkspace.shared.icon(forFile: url.path)
        icon.size = NSSize(width: 38, height: 38)

        let handler = ShortcutClickHandler(url: url)
        shortcutHandlers.append(handler)

        let button = ShortcutButton(
            title: url.lastPathComponent,
            target: handler,
            action: #selector(ShortcutClickHandler.openShortcut(_:))
        )
        button.image = icon
        button.imagePosition = .imageAbove
        button.bezelStyle = .regularSquare
        button.isBordered = false
        button.font = NSFont.systemFont(ofSize: 10)
        button.contentTintColor = .white
        button.lineBreakMode = .byTruncatingMiddle
        button.shortcutURL = url

        contentView.addSubview(button)
        shortcutButtons.append(button)

        button.onDragEnded = { [weak self] draggedButton, localPoint, screenPoint in
            guard let self else { return }
            if self.contentView.bounds.contains(localPoint) {
                self.moveShortcut(draggedButton, to: localPoint)
            } else {
                print("SHORTCUT DROPPED OUTSIDE PORTAL")
                print("Desktop position: \(NSStringFromPoint(screenPoint))")
                self.arrangeShortcuts()
                self.savePortalState()
            }
        }

        button.onRemove = { [weak self] buttonToRemove in
            self?.removeShortcut(buttonToRemove)
        }

        arrangeShortcuts()
    }

    // MARK: Alias Storage

    private static func storeAliasAndRemoveOriginal(_ sourceURL: URL) -> URL? {
        let fileManager = FileManager.default

        do {
            let applicationSupport = fileManager.urls(
                for: .applicationSupportDirectory,
                in: .userDomainMask
            )[0]
            let shortcutsFolder = applicationSupport
                .appendingPathComponent("Portals")
                .appendingPathComponent("Shortcuts")

            try fileManager.createDirectory(
                at: shortcutsFolder,
                withIntermediateDirectories: true
            )

            var destinationURL = shortcutsFolder.appendingPathComponent(
                sourceURL.lastPathComponent
            )
            var counter = 2

            while fileManager.fileExists(atPath: destinationURL.path) {
                let name = sourceURL.deletingPathExtension().lastPathComponent
                let ext = sourceURL.pathExtension
                var newName = "\(name) \(counter)"
                if !ext.isEmpty { newName += ".\(ext)" }
                destinationURL = shortcutsFolder.appendingPathComponent(newName)
                counter += 1
            }

            try fileManager.copyItem(at: sourceURL, to: destinationURL)
            try fileManager.removeItem(at: sourceURL)
            print("Alias moved into Portal storage: \(destinationURL.path)")
            return destinationURL
        } catch {
            print("Could not move alias: \(error)")
            return nil
        }
    }

    // MARK: Display

    func show() {
        window.makeKeyAndOrderFront(nil)
    }
}

// MARK: - Desktop Rectangle Selection

/// Transparent, mouse-ignoring window used only to draw the selection outline.
final class DesktopSelectionOverlayView: NSView {
    var selectionRect: NSRect = .zero

    override func draw(_ dirtyRect: NSRect) {
        guard !selectionRect.isEmpty else { return }

        let path = NSBezierPath(rect: selectionRect)
        path.lineWidth = 2

        NSColor.systemBlue.withAlphaComponent(0.20).setFill()
        path.fill()

        NSColor.systemBlue.withAlphaComponent(0.95).setStroke()
        path.stroke()
    }
}

final class DesktopSelectionOverlayWindow: NSWindow {
    private let selectionView: DesktopSelectionOverlayView
    private let screenFrame: NSRect

    init(screen: NSScreen) {
        screenFrame = screen.frame
        selectionView = DesktopSelectionOverlayView(
            frame: NSRect(origin: .zero, size: screen.frame.size)
        )

        super.init(
            contentRect: screen.frame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )

        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        ignoresMouseEvents = true
        level = NSWindow.Level(
            rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) + 2
        )
        collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        contentView = selectionView
    }

    func displaySelection(_ globalRect: NSRect) {
        selectionView.selectionRect = globalRect.offsetBy(
            dx: -screenFrame.origin.x,
            dy: -screenFrame.origin.y
        )
        selectionView.needsDisplay = true
        orderFrontRegardless()
    }
}

// Observes mouse gestures on the desktop and tracks rectangle selection.
final class DesktopSelectionManager {
    private var eventMonitor: Any?
    private var isSelecting = false
    private var isTrackingDesktopDrag = false
    private let dragThreshold: CGFloat = 10
    private var startPoint: NSPoint = .zero
    private var activeScreen: NSScreen?
    private var overlayWindow: DesktopSelectionOverlayWindow?

    private let onSelectionCompleted: (NSRect) -> Void

    init(onSelectionCompleted: @escaping (NSRect) -> Void) {
        self.onSelectionCompleted = onSelectionCompleted

        let eventMask: NSEvent.EventTypeMask = [
            .leftMouseDown,
            .leftMouseDragged,
            .leftMouseUp,
            .keyDown
        ]

        eventMonitor = NSEvent.addGlobalMonitorForEvents(matching: eventMask) {
            [weak self] event in
            guard let self else { return }

            if Thread.isMainThread {
                self.handle(event)
            } else {
                DispatchQueue.main.async {
                    self.handle(event)
                }
            }
        }

        if eventMonitor == nil {
            print("WARNING: Could not install the global mouse monitor.")
        } else {
            print("Desktop rectangle selection monitor installed")
        }
    }

    deinit {
        if let eventMonitor {
            NSEvent.removeMonitor(eventMonitor)
        }
    }

    private func handle(_ event: NSEvent) {

    switch event.type {

    case .leftMouseDown:
        beginSelectionIfOnDesktop(at: NSEvent.mouseLocation)

    case .leftMouseDragged:
        updateSelection(to: NSEvent.mouseLocation)

    case .leftMouseUp:
        if isTrackingDesktopDrag {
            if isSelecting {
                finishSelection(at: NSEvent.mouseLocation)
            } else {
                resetSelection()
            }
        }

    case .keyDown:
        if isTrackingDesktopDrag && event.keyCode == 53 {
            cancelSelection()
        }

    default:
        break
    }
}

    private func beginSelectionIfOnDesktop(at point: NSPoint) {

    guard !isTrackingDesktopDrag else { return }

    guard isDesktopBackground(at: point) else { return }

    guard let screen = NSScreen.screens.first(where: {
        $0.frame.contains(point)
    }) else {
        print("Could not identify the display")
        return
    }

    isTrackingDesktopDrag = true
    startPoint = point
    activeScreen = screen

    print("Desktop drag tracking started")
}

private func updateSelection(to point: NSPoint) {

    guard isTrackingDesktopDrag,
          let screen = activeScreen else { return }

    let distance = hypot(
        point.x - startPoint.x,
        point.y - startPoint.y
    )

    // Ignore ordinary clicks and tiny mouse movements.
    guard distance >= dragThreshold else { return }

    // Create the overlay only when a genuine drag begins.
    if !isSelecting {

        isSelecting = true

        let overlay = DesktopSelectionOverlayWindow(screen: screen)
        overlayWindow = overlay

        print("Desktop rectangle selection started")
    }

    let rect = selectionRect(to: point, limitedTo: screen.frame)
    overlayWindow?.displaySelection(rect)
}

private func resetSelection() {

    overlayWindow?.orderOut(nil)
    overlayWindow = nil

    activeScreen = nil
    isSelecting = false
    isTrackingDesktopDrag = false
}
   
private func finishSelection(at point: NSPoint) {
    guard isSelecting, let screen = activeScreen else { return }

    let rect = selectionRect(to: point, limitedTo: screen.frame)

    resetSelection()

    // Minimum Portal dimensions
    let minimumWidth: CGFloat = 250
    let minimumHeight: CGFloat = 180

    // Use the drawn size unless it is smaller than the minimum.
    let width = min(max(rect.width, minimumWidth), screen.frame.width)
    let height = min(max(rect.height, minimumHeight), screen.frame.height)

    // Keep the Portal within the selected monitor.
    let x = min(max(rect.minX, screen.frame.minX),
                screen.frame.maxX - width)

    let y = min(max(rect.minY, screen.frame.minY),
                screen.frame.maxY - height)

    let finalRect = NSRect(
        x: x,
        y: y,
        width: width,
        height: height
    )

    print("DESKTOP RECTANGLE READY: \(NSStringFromRect(finalRect))")

    onSelectionCompleted(finalRect)
}
 
 private func cancelSelection() {
    resetSelection()
    print("Desktop rectangle selection cancelled")
}

    private func selectionRect(to point: NSPoint, limitedTo screenFrame: NSRect) -> NSRect {
        let clampedX = min(max(point.x, screenFrame.minX), screenFrame.maxX)
        let clampedY = min(max(point.y, screenFrame.minY), screenFrame.maxY)

        return NSRect(
            x: min(startPoint.x, clampedX),
            y: min(startPoint.y, clampedY),
            width: abs(clampedX - startPoint.x),
            height: abs(clampedY - startPoint.y)
        )
    }
}

    

/// Checks whether the pointer is over the desktop background,
/// without depending on the Finder application name.
private func isDesktopBackground(at point: NSPoint) -> Bool {

    // Identify the monitor containing the pointer.
    guard let screen = NSScreen.screens.first(where: {
        $0.frame.contains(point)
    }) else {
        print("Could not identify display")
        return false
    }

    // Get this monitor's Quartz display coordinates.
    guard let screenNumber = screen.deviceDescription[
        NSDeviceDescriptionKey("NSScreenNumber")
    ] as? NSNumber else {
        print("Could not identify display number")
        return false
    }

    let displayID = CGDirectDisplayID(screenNumber.uint32Value)
    let displayBounds = CGDisplayBounds(displayID)

    // Convert AppKit coordinates to Quartz coordinates
    // using the actual monitor, not the primary monitor.
    let quartzPoint = CGPoint(
        x: displayBounds.origin.x + (point.x - screen.frame.origin.x),
        y: displayBounds.origin.y + (screen.frame.maxY - point.y)
    )

    print("MONITOR: \(screen.localizedName)")
print("APPKIT POINT: \(point)")
print("SCREEN FRAME: \(screen.frame)")
print("QUARTZ DISPLAY BOUNDS: \(displayBounds)")
print("CONVERTED QUARTZ POINT: \(quartzPoint)")

    guard let windowList = CGWindowListCopyWindowInfo(
        .optionOnScreenOnly,
        kCGNullWindowID
    ) as? [[String: Any]] else {
        print("Could not inspect on-screen windows")
        return false
    }

    let desktopIconLevel = Int(
        CGWindowLevelForKey(.desktopIconWindow)
    )

    // Examine windows from front to back.
    for info in windowList {

        let owner = info[kCGWindowOwnerName as String] as? String ?? ""

        // Ignore the Dock when checking desktop selection.
            if owner == "Dock" {
                continue
        }

        // Ignore system overlay entries.
        if owner == "Window Server" {
            continue
        }

        guard let boundsInfo = info[kCGWindowBounds as String] as? NSDictionary,
              let x = (boundsInfo["X"] as? NSNumber)?.doubleValue,
              let y = (boundsInfo["Y"] as? NSNumber)?.doubleValue,
              let width = (boundsInfo["Width"] as? NSNumber)?.doubleValue,
              let height = (boundsInfo["Height"] as? NSNumber)?.doubleValue,
              let layerNumber = info[kCGWindowLayer as String] as? NSNumber else {
            continue
        }

        let bounds = CGRect(
            x: x,
            y: y,
            width: width,
            height: height
        )

        guard bounds.contains(quartzPoint) else {
            continue
        }

        let layer = layerNumber.intValue

       if layer <= desktopIconLevel {
    print("DESKTOP BACKGROUND DETECTED")
    return true
} else {
    print("Application window detected: \(owner), layer \(layer)")
    return false
}
    }

    print("No window found at pointer")
    return false
}


  

// MARK: - Application

let app = NSApplication.shared
app.setActivationPolicy(.accessory)

// Retain Portal controllers for the lifetime of the application.
var portalControllers: [PortalController] = []



let savedPortals = PortalStore.loadAll()

NSApp.activate(ignoringOtherApps: true)

if savedPortals.isEmpty {

    // First run: create the default Portal.
    let firstPortal = PortalController(
        frame: NSRect(x: 150, y: 200, width: 340, height: 260),
        title: "Programming",
        restoreSavedState: false
    )

    portalControllers.append(firstPortal)
    firstPortal.show()
    firstPortal.savePortalState()

} else {

    // Restore every saved Portal.
    for state in savedPortals {

        let portal = PortalController(
            frame: NSRect(
                x: state.x,
                y: state.y,
                width: state.width,
                height: state.height
            ),
            title: state.title,
            restoreSavedState: false,
            portalID: state.id
        )

        portal.restorePortalState(state)

        portalControllers.append(portal)
        portal.show()
        portal.savePortalState()
    }
}


/// MARK: - Create Portal from Desktop Selection

let desktopSelectionManager = DesktopSelectionManager { selectedRect in

    print("Creating Portal at: \(NSStringFromRect(selectedRect))")

    let newPortal = PortalController(
        frame: selectedRect,
        title: "New Portal",
        restoreSavedState: false
    )

    portalControllers.append(newPortal)

newPortal.show()
newPortal.savePortalState()

print("New Portal created successfully")
}

app.run()
