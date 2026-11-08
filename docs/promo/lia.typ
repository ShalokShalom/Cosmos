#set page(
  paper: "a4",
  margin: 0mm,
  fill: rgb("#fff8e1"),
)

#set text(font: "DejaVu Sans", size: 13pt, fill: rgb("#1e1e2e"))

#let yellow = rgb("#f9e2af")
#let pink = rgb("#f5c2e7")
#let mauve = rgb("#cba6f7")
#let dark = rgb("#1e1e2e")
#let cream = rgb("#fffdf5")

#place(top + left, dx: 16mm, dy: 14mm)[
  #text(size: 11pt, weight: "bold", fill: mauve)[MEET LIA]
]

#place(top + left, dx: 16mm, dy: 23mm)[
  #text(size: 28pt, weight: "bold", fill: dark)[
    Lia finds the #text(fill: rgb("#b8860b"))[missing pieces!]
  ]
]

#place(top + left, dx: 16mm, dy: 40mm)[
  #box(width: 92mm, height: 92mm)[
    // Tail
    #place(dx: 68mm, dy: 52mm)[
      #ellipse(width: 18mm, height: 28mm, fill: yellow, stroke: dark + 1.4pt)
    ]
    // Body
    #place(dx: 26mm, dy: 45mm)[
      #ellipse(width: 42mm, height: 35mm, fill: yellow, stroke: dark + 1.4pt)
    ]
    // Ears
    #place(dx: 12mm, dy: 13mm)[
      #polygon(
        fill: yellow,
        stroke: dark + 1.4pt,
        (0mm, 18mm),
        (10mm, 0mm),
        (22mm, 17mm),
      )
    ]
    #place(dx: 47mm, dy: 13mm)[
      #polygon(
        fill: yellow,
        stroke: dark + 1.4pt,
        (0mm, 17mm),
        (12mm, 0mm),
        (22mm, 18mm),
      )
    ]
    // Head
    #place(dx: 23mm, dy: 22mm)[
      #ellipse(width: 44mm, height: 39mm, fill: yellow, stroke: dark + 1.4pt)
    ]
    // Antenna and star
    #place(dx: 43mm, dy: 3mm)[
      #line(start: (0mm, 13mm), end: (0mm, 1mm), stroke: dark + 1.4pt)
    ]
    #place(dx: 37mm, dy: 0mm)[
      #text(size: 25pt, fill: rgb("#d99a00"))[★]
    ]
    // Eyes
    #place(dx: 34mm, dy: 35mm)[
      #circle(radius: 3.6mm, fill: dark)
    ]
    #place(dx: 53mm, dy: 35mm)[
      #circle(radius: 3.6mm, fill: dark)
    ]
    #place(dx: 35mm, dy: 34mm)[
      #circle(radius: 1.2mm, fill: white)
    ]
    #place(dx: 54mm, dy: 34mm)[
      #circle(radius: 1.2mm, fill: white)
    ]
    // Cheeks
    #place(dx: 28mm, dy: 45mm)[
      #ellipse(width: 8mm, height: 4mm, fill: pink)
    ]
    #place(dx: 56mm, dy: 45mm)[
      #ellipse(width: 8mm, height: 4mm, fill: pink)
    ]
    // Nose and smile
    #place(dx: 43mm, dy: 44mm)[
      #polygon(fill: dark, (0mm, 0mm), (5mm, 0mm), (2.5mm, 3mm))
    ]
    #place(dx: 38mm, dy: 49mm)[
      #text(size: 22pt, fill: dark)[◡]
    ]
    // Paws
    #place(dx: 32mm, dy: 77mm)[
      #ellipse(width: 12mm, height: 7mm, fill: yellow, stroke: dark + 1.4pt)
    ]
    #place(dx: 54mm, dy: 77mm)[
      #ellipse(width: 12mm, height: 7mm, fill: yellow, stroke: dark + 1.4pt)
    ]
  ]
]

#place(top + left, dx: 112mm, dy: 43mm)[
  #block(
    width: 78mm,
    inset: 6mm,
    radius: 5mm,
    fill: cream,
    stroke: mauve + 2pt,
  )[
    #text(size: 17pt, weight: "bold", fill: mauve)[
      “When two ideas fit,#linebreak()
      I say: snap!”
    ]
    #v(2mm)
    #text(size: 12pt)[
      I am Lia. I look for pieces that belong together.
      That is called #text(weight: "bold", fill: rgb("#b8860b"))[unification].
    ]
  ]
]

// Three small ideas
#place(top + left, dx: 16mm, dy: 141mm)[
  #box(width: 178mm)[
    #grid(
      columns: (1fr, 1fr, 1fr),
      gutter: 5mm,
      [
        #block(
          width: 100%,
          inset: 5mm,
          radius: 4mm,
          fill: cream,
          stroke: pink + 1.5pt,
        )[
          #text(size: 11pt, weight: "bold", fill: pink)[1. ASK]
          #v(1mm)
          #text(size: 11pt)[A question is a little puzzle. What fits?]
        ]
      ],
      [
        #block(
          width: 100%,
          inset: 5mm,
          radius: 4mm,
          fill: cream,
          stroke: mauve + 1.5pt,
        )[
          #text(size: 11pt, weight: "bold", fill: mauve)[2. MATCH]
          #v(1mm)
          #text(size: 11pt)[Lia tries a piece. If it fits, it clicks!]
        ]
      ],
      [
        #block(
          width: 100%,
          inset: 5mm,
          radius: 4mm,
          fill: cream,
          stroke: rgb("#b8860b") + 1.5pt,
        )[
          #text(size: 11pt, weight: "bold", fill: rgb("#b8860b"))[3. SMILE]
          #v(1mm)
          #text(size: 11pt)[Every match is a tiny discovery.]
        ]
      ],
    )
  ]
]

// Tiny puzzle card
#place(top + left, dx: 16mm, dy: 203mm)[
  #block(
    width: 178mm,
    inset: 6mm,
    radius: 4mm,
    fill: yellow,
    stroke: dark + 1.5pt,
  )[
    #text(size: 13pt, weight: "bold")[Lia's puzzle card]
    #v(1mm)
    #text(size: 11pt)[
      Mia has a friend. Who is it? Lia finds the piece that fits.
    ]
    #v(2mm)
    #text(font: "DejaVu Sans Mono", size: 11pt)[
      friend(mia, luna).#linebreak()
      ?- friend(mia, ?who).#linebreak()
      → who = luna
    ]
  ]
]

#place(top + left, dx: 16mm, dy: 270mm)[
  #text(size: 10pt, fill: rgb("#555555"))[
    Lia is the #text(weight: "bold", fill: rgb("#b8860b"))[Unification] mascot.
    She loves it when ideas fit together. • Scale: a language for curious minds.
  ]
]
