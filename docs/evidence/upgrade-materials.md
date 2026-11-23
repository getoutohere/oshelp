# Extracted upgrade material requirements

All 131 upgrade definitions, including zero-cost starting states. Quantities, OC deposits, RM final payments, and hours are raw asset values before difficulty adjustment. A prerequisite is an explicit asset reference, not an inferred previous tier. Names in the materials column are resolved by item ID from `Items.json`. See [catalog.json](catalog.json) and [rules.il.txt](rules.il.txt).

| Upgrade | Materials | Prerequisites | Start OC | Final RM | Base hours |
|---|---|---|---:|---:|---:|
| Large-Balcony-Back-0-Bare |  |  | 0 | 0 | 0 |
| Large-Balcony-Back-1-Makeshift | 20 Plank; 10 Sheet Metal; 20 Nails; 20 Scrap Metal; 2 Cloth Roll |  | 2000 | 40 | 50 |
| Large-Balcony-Back-2-Shabby | 50 Plank; 5 Sheet Metal; 50 Nails; 10 Scrap Metal; 5 Glass Pane |  | 3500 | 210 | 100 |
| Large-Balcony-Back-3-Decent | 10 Plank; 5 Sheet Metal; 50 Nails; 10 Glass Pane; 5 Cement; 10 Rebar | Small-Electricity-1-On | 40000 | 1000 | 192 |
| Large-Balcony-Back-4-Fine | 5 Plank; 10 Sheet Metal; 80 Nails; 15 Glass Pane; 10 Cement; 5 Bucket of Paint; 15 Rebar | Small-Electricity-1-On | 60000 | 3000 | 240 |
| Large-Balcony-Back-5-Luxurious |  | Small-Electricity-1-On | 0 | 0 | 390 |
| Player-Extension-Back-0-None |  |  | 0 | 0 | 0 |
| Player-Extension-Back-1-Makeshift | 20 Plank; 10 Sheet Metal; 20 Nails; 20 Scrap Metal; 2 Cloth Roll | Courtyard-1-Makeshift | 2400 | 300 | 96 |
| Player-Extension-Back-2-Shabby | 50 Plank; 5 Sheet Metal; 50 Nails; 10 Scrap Metal; 5 Glass Pane | Courtyard-1-Makeshift; Small-Electricity-1-On | 9000 | 1000 | 168 |
| Player-Extension-Back-3-Decent | 10 Plank; 5 Sheet Metal; 50 Nails; 10 Glass Pane; 5 Cement; 3 Asbestos | Courtyard-1-Makeshift; Small-Electricity-1-On; Facade-2-Shabby | 40000 | 4000 | 192 |
| Player-Extension-Back-4-Fine | 5 Plank; 10 Sheet Metal; 80 Nails; 15 Glass Pane; 10 Cement; 5 Bucket of Paint; 2 Insulation Wool | Courtyard-1-Makeshift; Small-Electricity-1-On; Facade-3-Decent | 60000 | 8000 | 240 |
| Player-Extension-Back-5-Luxurious | 10 Plank; 20 Sheet Metal; 200 Nails; 50 Glass Pane; 12 Cement; 5 Bucket of Paint | Courtyard-1-Makeshift; Small-Electricity-1-On | 0 | 0 | 390 |
| Player-Extension-Front-0-None |  |  | 0 | 0 | 0 |
| Player-Extension-Front-1-Makeshift | 20 Plank; 10 Sheet Metal; 20 Nails; 20 Scrap Metal; 2 Cloth Roll; 10 Rock | Courtyard-1-Makeshift | 2400 | 300 | 96 |
| Player-Extension-Front-2-Shabby | 50 Plank; 5 Sheet Metal; 50 Nails; 10 Scrap Metal; 5 Glass Pane; 10 Rock; 1 Gravel; 15 Brick | Courtyard-1-Makeshift; Small-Electricity-1-On | 9000 | 1000 | 168 |
| Player-Extension-Front-3-Decent | 10 Plank; 5 Sheet Metal; 50 Nails; 10 Glass Pane; 5 Cement; 3 Asbestos; 8 Gravel; 20 Brick | Courtyard-1-Makeshift; Small-Electricity-1-On; Facade-2-Shabby | 40000 | 4000 | 192 |
| Player-Extension-Front-4-Fine | 5 Plank; 10 Sheet Metal; 80 Nails; 15 Glass Pane; 10 Cement; 5 Bucket of Paint; 2 Insulation Wool; 10 Gravel | Courtyard-1-Makeshift; Small-Electricity-1-On; Facade-3-Decent | 60000 | 8000 | 240 |
| Player-Extension-Front-5-Luxurious | 10 Plank; 20 Sheet Metal; 200 Nails; 50 Glass Pane; 12 Cement; 5 Bucket of Paint | Courtyard-1-Makeshift; Small-Electricity-1-On | 0 | 0 | 390 |
| Shop-Bathroom-0-Bare |  |  | 0 | 0 | 0 |
| Shop-Bathroom-1-Makeshift | 2 Plank; 4 Nails; 5 Scrap Metal; 5 Tarp |  | 320 | 20 | 8 |
| Shop-Bathroom-2-Shabby | 10 Plank; 20 Nails; 10 Scrap Metal; 1 Glass Pane; 3 Pipes; 1 Cable; 2 Tiles | Small-Electricity-1-On; Small-Water-1-Shit | 4500 | 350 | 24 |
| Shop-Bathroom-3-Decent | 4 Plank; 8 Nails; 30 Scrap Metal; 2 Cement; 6 Pipes; 1 Cable; 12 Tiles; 1 Wiring Components | Small-Electricity-1-On; Small-Water-2-Good | 16000 | 1300 | 72 |
| Shop-Bathroom-4-Fine | 2 Plank; 4 Nails; 30 Scrap Metal; 2 Cement; 8 Pipes; 1 Cable; 2 Glass Pane; 2 Wiring Components; 20 Tiles; 1 Mechanical Parts | Small-Electricity-1-On; Small-Water-3-Best | 50000 | 4000 | 144 |
| Shop-Bathroom-5-Luxurious |  | Small-Electricity-1-On; Small-Water-3-Best | 0 | 0 | 100 |
| Small-Balcony-0-None |  |  | 0 | 0 | 0 |
| Small-Balcony-1-Makeshift | 20 Plank; 10 Sheet Metal; 20 Nails; 20 Scrap Metal; 2 Cloth Roll |  | 2000 | 40 | 50 |
| Small-Balcony-2-Shabby | 50 Plank; 5 Sheet Metal; 50 Nails; 10 Scrap Metal; 5 Glass Pane |  | 3500 | 210 | 100 |
| Small-Balcony-3-Decent | 10 Plank; 5 Sheet Metal; 50 Nails; 10 Glass Pane; 5 Cement; 10 Rebar | Small-Electricity-1-On | 40000 | 1000 | 192 |
| Small-Balcony-4-Fine | 5 Plank; 10 Sheet Metal; 80 Nails; 15 Glass Pane; 10 Cement; 5 Bucket of Paint; 15 Rebar | Small-Electricity-1-On | 60000 | 3000 | 240 |
| Small-Balcony-5-Luxurious | 10 Plank; 20 Sheet Metal; 200 Nails; 50 Glass Pane; 12 Cement; 5 Bucket of Paint | Small-Electricity-1-On | 0 | 0 | 390 |
| Small-Bathroom-0-Bare |  |  | 0 | 0 | 0 |
| Small-Bathroom-1-Makeshift | 2 Plank; 4 Nails; 5 Scrap Metal; 5 Tarp |  | 320 | 20 | 8 |
| Small-Bathroom-2-Shabby | 10 Plank; 20 Nails; 10 Scrap Metal; 1 Glass Pane; 3 Pipes; 1 Cable; 2 Tiles | Small-Electricity-1-On; Small-Water-1-Shit | 4500 | 350 | 24 |
| Small-Bathroom-3-Decent | 4 Plank; 8 Nails; 30 Scrap Metal; 2 Cement; 6 Pipes; 1 Cable; 12 Tiles; 1 Wiring Components | Small-Electricity-1-On; Small-Water-2-Good | 16000 | 1300 | 72 |
| Small-Bathroom-4-Fine | 2 Plank; 4 Nails; 30 Scrap Metal; 2 Cement; 8 Pipes; 1 Cable; 2 Glass Pane; 2 Wiring Components; 20 Tiles; 1 Mechanical Parts | Small-Electricity-1-On; Small-Water-3-Best | 50000 | 4000 | 144 |
| Small-Bathroom-5-Luxurious |  | Small-Electricity-1-On; Small-Water-3-Best | 0 | 0 | 100 |
| Small-Electricity-0-Off |  |  | 0 | 0 | 0 |
| Small-Electricity-1-On | 6 Cable; 2 Cement; 10 Scrap Metal; 6 Wiring Components; 2 Electronic Parts | Small-Walls-1-Makeshift; Electricity-1-Makeshift | 1500 | 120 | 24 |
| Small-Heating-0-None |  |  | 0 | 0 | 0 |
| Small-Heating-1-Makeshift | 3 Pipes; 2 Sheet Metal; 8 Scrap Metal |  | 600 | 30 | 24 |
| Small-Heating-2-Shabby | 3 Pipes; 2 Sheet Metal; 12 Scrap Metal; 1 Bucket of Paint; 1 Mechanical Parts |  | 1500 | 120 | 48 |
| Small-Heating-3-Decent | 12 Pipes; 5 Sheet Metal; 40 Scrap Metal; 5 Cement; 5 Bucket of Paint; 2 Mechanical Parts | Heating-2-Central | 16300 | 2200 | 120 |
| Small-Heating-4-Fine | 12 Pipes; 10 Sheet Metal; 60 Scrap Metal; 10 Cement; 5 Bucket of Paint; 15 Nails; 8 Mechanical Parts | Heating-2-Central | 40200 | 5000 | 192 |
| Small-Heating-5-Luxurious |  | Heating-3-District | 0 | 0 | 384 |
| Small-Kitchen-0-None |  |  | 0 | 0 | 0 |
| Small-Kitchen-1-Makeshift | 2 Plank; 8 Nails; 25 Scrap Metal; 2 Duct Tape | Small-Walls-1-Makeshift | 1550 | 50 | 12 |
| Small-Kitchen-2-Shabby | 10 Plank; 25 Nails; 20 Scrap Metal; 2 Duct Tape; 2 Cement; 2 Plywood; 1 Tiles | Small-Electricity-1-On; Small-Water-1-Shit | 4100 | 260 | 36 |
| Small-Kitchen-3-Decent | 2 Plank; 4 Nails; 20 Scrap Metal; 1 Electronic Parts; 1 Bucket of Paint; 4 Cement; 4 Plywood; 1 Mechanical Parts; 4 Tiles | Small-Electricity-1-On; Small-Water-2-Good | 13000 | 1200 | 72 |
| Small-Kitchen-4-Fine | 4 Plank; 10 Nails; 25 Scrap Metal; 2 Electronic Parts; 1 Bucket of Paint; 4 Cement; 8 Plywood; 2 Mechanical Parts; 8 Tiles | Small-Electricity-1-On; Small-Water-3-Best | 25000 | 3000 | 144 |
| Small-Kitchen-5-Luxurious |  | Small-Electricity-1-On; Small-Water-3-Best | 0 | 0 | 100 |
| Small-Walls-0-Bare |  |  | 0 | 0 | 0 |
| Small-Walls-1-Makeshift | 30 Plank; 30 Nails; 15 Scrap Metal; 4 Duct Tape; 2 Tarp |  | 800 | 75 | 24 |
| Small-Walls-2-Shabby | 20 Plank; 20 Nails; 10 Scrap Metal; 4 Tarp; 2 Cloth Roll; 1 Cable; 5 Duct Tape; 4 Plywood | Small-Electricity-1-On; Small-Walls-1-Makeshift | 2700 | 290 | 48 |
| Small-Walls-3-Decent | 15 Plank; 30 Nails; 15 Scrap Metal; 2 Glass Pane; 1 Cloth Roll; 2 Cable; 1 Wiring Components; 12 Plywood; 1 Bucket of Paint | Small-Electricity-1-On | 25000 | 2500 | 144 |
| Small-Walls-4-Fine | 15 Plank; 30 Nails; 15 Scrap Metal; 2 Sand; 1 Cloth Roll; 2 Cable; 4 Bucket of Paint; 2 Wiring Components; 20 Plywood; 12 Furniture Parts | Small-Electricity-1-On | 50000 | 5000 | 216 |
| Small-Walls-5-Luxurious |  | Small-Electricity-1-On | 0 | 0 | 100 |
| Small-Water-0-None |  |  | 0 | 0 | 0 |
| Small-Water-1-Shit | 6 Pipes; 16 Scrap Metal; 8 Cement; 4 Duct Tape | Water-1-Dirty | 2500 | 200 | 52 |
| Small-Water-2-Good | 10 Pipes; 30 Scrap Metal; 10 Cement; 1 Orange Handwheel; 1 Asbestos; 10 Plastic Parts | Water-2-Good | 7500 | 700 | 96 |
| Small-Water-3-Best | 10 Pipes; 30 Scrap Metal; 10 Cement; 10 Mechanical Parts; 1 Insulation Wool; 8 Plastic Parts | Water-3-Best | 40000 | 3000 | 144 |
| Basement-0-Bare |  |  | 0 | 0 | 0 |
| Basement-1-Makeshift | 6 Cement; 4 Plywood; 4 Plank; 8 Nails; 8 Scrap Metal; 4 Duct Tape; 2 Tarp; 2 Rebar |  | 6500 | 100 | 48 |
| Basement-2-Shabby | 6 Cement; 6 Sheet Metal; 20 Plank; 40 Nails; 12 Scrap Metal; 6 Duct Tape; 2 Cloth Roll; 3 Rebar | Electricity-1-Makeshift | 15500 | 250 | 72 |
| Basement-3-Decent | 10 Cement; 6 Sheet Metal; 6 Plank; 12 Nails; 26 Scrap Metal; 12 Bucket of Paint; 2 Cloth Roll; 7 Rebar; 20 Brick | Electricity-1-Makeshift | 60000 | 1400 | 128 |
| Basement-4-Fine | 12 Cement; 6 Sheet Metal; 3 Plank; 8 Nails; 20 Scrap Metal; 20 Bucket of Paint; 2 Cloth Roll; 10 Rebar | Electricity-1-Makeshift | 100000 | 8000 | 192 |
| Basement-5-Luxurious |  | Electricity-2-Good | 0 | 0 | 100 |
| Courtyard-0-Bare |  |  | 0 | 0 | 0 |
| Courtyard-1-Makeshift | 40 Plank; 80 Nails; 5 Gravel |  | 6800 | 70 | 48 |
| Courtyard-2-Shabby | 40 Plank; 80 Nails; 5 Sand; 4 Cement; 1 Tarp; 1 Mesh; 1 Cable; 20 Gravel | Courtyard-1-Makeshift; Electricity-1-Makeshift | 13000 | 250 | 50 |
| Courtyard-3-Decent | 20 Plank; 40 Nails; 10 Sheet Metal; 8 Cement; 5 Fertile Soil; 5 Cable; 20 Brick; 10 Gravel | Courtyard-2-Shabby; Facade-2-Shabby; Electricity-1-Makeshift | 60000 | 5000 | 72 |
| Courtyard-4-Fine | 40 Plank; 40 Nails; 12 Sheet Metal; 16 Cement; 4 Bucket of Paint; 10 Cable; 5 Wiring Components; 50 Brick; 5 Gravel; 2 Mesh; 12 Rebar | Courtyard-3-Decent; Electricity-2-Good | 100000 | 10000 | 100 |
| Courtyard-5-Luxurious |  | Courtyard-4-Fine | 0 | 0 | 100 |
| Electricity-0-None |  |  | 0 | 0 | 0 |
| Electricity-1-Makeshift | 10 Cable; 20 Scrap Metal; 2 Sheet Metal; 2 Electronic Parts; 1 Wiring Components; 4 Plastic Parts | Basement-1-Makeshift | 5215 | 180 | 24 |
| Electricity-2-Good | 12 Cable; 35 Scrap Metal; 6 Sheet Metal; 10 Electronic Parts; 4 Computer Parts; 4 Industrial Fuse; 10 Wiring Components; 10 Plastic Parts | Basement-1-Makeshift; Electricity-1-Makeshift | 18500 | 900 | 72 |
| Electricity-3-Best | 12 Cable; 35 Scrap Metal; 6 Sheet Metal; 10 Electronic Parts; 4 Computer Parts; 4 Industrial Fuse; 25 Wiring Components; 25 Plastic Parts | Basement-1-Makeshift; Electricity-2-Good | 60000 | 10000 | 216 |
| Elevator-0-Off |  |  | 0 | 0 | 0 |
| Elevator-1-On | 12 Rail; 1 Electric Motor; 20 Cable; 4 Industrial Fuse; 12 Wiring Components; 25 Sheet Metal; 20 Mechanical Parts; 10 Electronic Parts; 10 Mesh | Stairwell-3-Decent; Electricity-2-Good | 20000 | 5900 | 125 |
| Elevator-2-Large | 20 Rail; 1 Electric Motor; 20 Cable; 4 Industrial Fuse; 15 Wiring Components; 10 Sheet Metal; 20 Mechanical Parts; 10 Electronic Parts; 20 Stainless Steel; 5 Computer Parts; 15 Glass Pane | Stairwell-4-Fine; Electricity-3-Best | 35000 | 11500 | 200 |
| Facade-0-Bare |  |  | 0 | 0 | 0 |
| Facade-1-Makeshift | 10 Cement; 10 Scrap Metal |  | 2000 | 50 | 48 |
| Facade-2-Shabby | 15 Cement; 15 Scrap Metal; 3 Plank; 5 Asbestos; 8 Rebar | Facade-1-Makeshift; Electricity-1-Makeshift | 14500 | 1700 | 96 |
| Facade-3-Decent | 20 Cement; 20 Scrap Metal; 15 Asbestos; 15 Rebar | Facade-2-Shabby; Electricity-1-Makeshift | 40000 | 10000 | 168 |
| Facade-4-Fine | 20 Cement; 30 Scrap Metal; 15 Bucket of Paint; 15 Insulation Wool; 15 Rebar | Facade-3-Decent; Electricity-2-Good | 100000 | 20000 | 216 |
| Facade-5-Luxurious |  | Facade-4-Fine; Electricity-2-Good | 0 | 0 | 100 |
| Heating-0-None |  |  | 0 | 0 | 0 |
| Heating-1-Bare | 30 Firewood; 10 Nails; 15 Scrap Metal; 10 Plank | Basement-1-Makeshift | 900 | 50 | 24 |
| Heating-2-Central | 15 Sheet Metal; 25 Pipes; 30 Scrap Metal; 2 Cable; 5 Plank; 2 Wiring Components; 2 Mechanical Parts; 12 Asbestos; 4 Orange Handwheel | Basement-1-Makeshift; Electricity-1-Makeshift | 23000 | 2100 | 100 |
| Heating-3-District | 30 Sheet Metal; 50 Pipes; 50 Scrap Metal; 2 Cable; 5 Electronic Parts; 3 Computer Parts; 5 Wiring Components; 10 Mechanical Parts; 12 Insulation Wool; 4 Orange Handwheel; 8 Stainless Steel | Basement-1-Makeshift; Electricity-2-Good | 100000 | 15000 | 100 |
| Mines-0-None |  |  | 0 | 0 | 0 |
| Mines-1-Makeshift | 20 Plank; 5 Nails; 2 Sheet Metal; 8 Dynamite; 5 Wood Log; 8 Rail | Basement-1-Makeshift | 3000 | 200 | 24 |
| Mines-2-Shabby | 40 Plank; 15 Nails; 2 Sheet Metal; 1 Wiring Components; 5 Mesh; 3 Gravel; 10 Dynamite; 15 Wood Log; 4 Rail | Mines-1-Makeshift; Electricity-1-Makeshift | 10000 | 300 | 72 |
| Mines-3-Decent | 70 Plank; 30 Nails; 2 Sheet Metal; 2 Wiring Components; 1 Cable; 12 Dynamite; 32 Wood Log; 16 Rail | Mines-2-Shabby; Electricity-2-Good | 50000 | 1500 | 128 |
| Mines-4-Fine | 20 Cement; 4 Sheet Metal; 10 Scrap Metal; 4 Wiring Components; 2 Cable; 6 Pipes; 1 Mechanical Parts; 1 Electronic Parts; 12 Dynamite; 12 Rail; 20 Rebar; 10 Tiles | Mines-3-Decent; Electricity-3-Best; Water-1-Dirty | 120000 | 3000 | 192 |
| Roof-0-None |  |  | 0 | 0 | 0 |
| Roof-1-Shabby | 6 Cement; 60 Glass Pane; 20 Sheet Metal; 20 Plank; 35 Nails; 20 Scrap Metal; 4 Rebar; 2 Wiring Components | Stairwell-3-Decent | 6000 | 300 | 50 |
| Roof-2-Decent | 6 Cement; 80 Glass Pane; 30 Sheet Metal; 2 Plank; 10 Nails; 40 Scrap Metal; 5 Rebar; 15 Tiles; 4 Bucket of Paint; 2 Wiring Components; 4 Pipes | Roof-1-Shabby | 10000 | 1500 | 72 |
| Roof-3-Fine | 6 Cement; 120 Glass Pane; 60 Sheet Metal; 4 Plank; 15 Nails; 80 Scrap Metal; 4 Rebar; 30 Tiles; 15 Bucket of Paint; 20 Pipes; 4 Wiring Components; 4 Mechanical Parts | Roof-2-Decent | 50000 | 10000 | 100 |
| Sauna and pier-0-Bare |  |  | 0 | 0 | 0 |
| Sauna and pier-1-Makeshift | 40 Plank; 80 Nails; 1 Leather Roll; 20 Scrap Metal; 2 Pipes; 4 Duct Tape; 4 Sheet Metal; 1 Bucket of Paint; 4 Rock |  | 15000 | 800 | 48 |
| Sauna and pier-2-Shabby | 60 Plank; 120 Nails; 2 Cloth Roll; 30 Scrap Metal; 3 Pipes; 8 Duct Tape; 4 Sheet Metal |  | 21500 | 1700 | 96 |
| Sauna and pier-3-Decent | 30 Plank; 80 Nails; 2 Cloth Roll; 20 Scrap Metal; 6 Pipes; 2 Duct Tape; 8 Sheet Metal; 8 Cement; 4 Glass Pane; 3 Bucket of Paint; 8 Rebar; 8 Tiles |  | 60000 | 10000 | 144 |
| Sauna and pier-4-Fine | 60 Plank; 160 Nails; 2 Cloth Roll; 30 Scrap Metal; 6 Pipes; 2 Duct Tape; 8 Sheet Metal; 14 Cement; 6 Glass Pane; 6 Bucket of Paint; 14 Rebar; 26 Tiles |  | 60000 | 20000 | 192 |
| Sauna and pier-5-Luxurious |  |  | 0 | 0 | 100 |
| Sauna-0-Bare |  |  | 0 | 0 | 0 |
| Sauna-1-Makeshift | 12 Plank; 20 Nails; 2 Sheet Metal; 16 Scrap Metal; 4 Pipes; 2 Leather Roll | Basement-1-Makeshift | 870 | 80 | 24 |
| Sauna-2-Shabby | 30 Plank; 35 Nails; 2 Sheet Metal; 24 Scrap Metal; 4 Pipes; 2 Leather Roll; 2 Cloth Roll; 6 Tiles | Basement-1-Makeshift; Electricity-1-Makeshift; Water-1-Dirty | 9500 | 400 | 38 |
| Sauna-3-Decent | 30 Plank; 40 Nails; 2 Sheet Metal; 34 Scrap Metal; 4 Pipes; 4 Cement; 2 Bucket of Paint; 35 Tiles | Basement-1-Makeshift; Electricity-2-Good; Heating-2-Central; Water-1-Dirty | 50000 | 2000 | 96 |
| Sauna-4-Fine | 35 Plank; 45 Nails; 2 Sheet Metal; 35 Scrap Metal; 4 Pipes; 4 Cement; 4 Bucket of Paint; 2 Glass Pane; 60 Tiles | Basement-1-Makeshift; Electricity-2-Good; Heating-2-Central; Water-2-Good | 80000 | 10000 | 120 |
| Sauna-5-Luxurious |  | Basement-1-Makeshift; Electricity-2-Good | 0 | 0 | 100 |
| Shrooms-0-Bare |  |  | 0 | 0 | 0 |
| Shrooms-1-Makeshift | 16 Plank; 26 Nails; 2 Sheet Metal; 12 Scrap Metal; 1 Leather Roll | Basement-1-Makeshift | 3000 | 400 | 24 |
| Shrooms-2-Shabby | 24 Plank; 40 Nails; 2 Sheet Metal; 20 Scrap Metal; 4 Leather Roll; 15 Dynamite | Basement-1-Makeshift; Electricity-1-Makeshift; Shrooms-1-Makeshift | 8900 | 700 | 72 |
| Shrooms-3-Decent | 15 Cement; 4 Pipes; 10 Sheet Metal; 30 Scrap Metal; 4 Bucket of Paint; 13 Rebar; 16 Dynamite | Basement-1-Makeshift; Electricity-1-Makeshift; Shrooms-2-Shabby | 20000 | 3000 | 96 |
| Shrooms-4-Fine | 4 Cement; 4 Pipes; 10 Sheet Metal; 30 Scrap Metal; 10 Cloth Roll; 3 Rebar; 6 Dynamite; 40 Tiles | Basement-1-Makeshift; Electricity-1-Makeshift; Shrooms-3-Decent | 40000 | 5000 | 144 |
| Shrooms-5-Luxurious |  | Basement-1-Makeshift; Electricity-2-Good; Shrooms-4-Fine | 0 | 0 | 100 |
| Stairwell-0-Bare |  |  | 0 | 0 | 0 |
| Stairwell-1-Makeshift | 2 Cement; 20 Plank; 15 Nails; 12 Scrap Metal; 4 Glass Pane |  | 3500 | 175 | 24 |
| Stairwell-2-Shabby | 2 Cement; 50 Plank; 80 Nails; 25 Scrap Metal; 6 Glass Pane; 14 Plywood | Stairwell-1-Makeshift; Electricity-1-Makeshift | 13500 | 400 | 48 |
| Stairwell-3-Decent | 8 Cement; 15 Bucket of Paint; 8 Sheet Metal; 30 Scrap Metal; 8 Glass Pane; 8 Rebar; 25 Tiles | Stairwell-2-Shabby; Electricity-1-Makeshift | 50000 | 2000 | 72 |
| Stairwell-4-Fine | 14 Cement; 20 Bucket of Paint; 12 Sheet Metal; 30 Scrap Metal; 12 Glass Pane; 12 Rebar; 40 Tiles | Stairwell-3-Decent; Electricity-1-Makeshift | 80000 | 7000 | 100 |
| Stairwell-5-Luxurious |  | Stairwell-4-Fine; Electricity-2-Good | 0 | 0 | 100 |
| Water-0-None |  |  | 0 | 0 | 0 |
| Water-1-Dirty | 40 Pipes; 60 Scrap Metal; 2 Sheet Metal; 6 Duct Tape; 1 Orange Handwheel; 5 Plastic Parts; 5 Mechanical Parts; 1 Cable | Basement-1-Makeshift; Electricity-1-Makeshift | 5600 | 120 | 48 |
| Water-2-Good | 50 Pipes; 80 Scrap Metal; 10 Sheet Metal; 2 Wiring Components; 6 Electronic Parts; 2 Cable; 20 Plastic Parts; 2 Orange Handwheel; 5 Asbestos; 10 Mechanical Parts | Basement-1-Makeshift; Electricity-2-Good | 14500 | 3200 | 120 |
| Water-3-Best | 30 Pipes; 20 Scrap Metal; 15 Sheet Metal; 5 Orange Handwheel; 10 Stainless Steel; 35 Plastic Parts; 10 Insulation Wool; 28 Mechanical Parts; 10 Computer Parts; 4 Wiring Components; 5 Cable; 3 Electronic Parts | Basement-1-Makeshift; Electricity-2-Good; Water-2-Good | 50000 | 15000 | 168 |
| Workshop-0-None |  |  | 0 | 0 | 0 |
| Workshop-1-Small | 4 Cement; 10 Plank; 2 Rebar | Basement-1-Makeshift | 4000 | 600 | 24 |
| Workshop-2-Medium | 8 Cement; 10 Plank; 4 Rebar | Workshop-1-Small | 8000 | 1200 | 24 |
| Workshop-3-Large | 4 Cement; 10 Plank; 8 Scrap Metal; 2 Sheet Metal; 2 Nails; 3 Rebar | Workshop-2-Medium | 12000 | 3000 | 48 |
| Workshop-4-Huge | 32 Cement; 20 Plank; 10 Mesh; 8 Sheet Metal; 24 Nails; 30 Scrap Metal; 50 Brick; 30 Rebar; 25 Dynamite | Workshop-3-Large; Basement-2-Shabby | 16000 | 4000 | 52 |
