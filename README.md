# PADSToARESFileConverter
Converts PADS PowerPCB model files into Proteus ARES format

Proteus ARES is not that common EDA tool, especially ARES 7.x (that lacks any builtin import options), therefore it's hard to find component models (footprints) for it. Sometimes, for example when you need a PCI connector model, or something exotic like FX8-120S PCB interconnects, it's too teadious to draw the footprint by hand. On the other hand, Proteus design suite has a decent digital simulation (and netlist export to PCB design) capabilities and it's hard to give it up for some other EDA software.

This tool allows to convert simple "TH/SMT pins & polyline graphics" footprints from PADS PowerPCB format (which is popular, for example, at componentsearchengine.com) to ARES Region format.

Since it's still under development (though it helps me on a regular basis), so I've not implemented any interface yet. You are supposed to specify file locations in Main(...).

Todo:
 - Commandline interface
 - Complicated graphics (arcs and circles)
 
Complications:
 - Since ARES doesn't import pad/trace styles from region files, you have to create them by hand before you open the region file (otherwise default styles are used and all the dimensions end up being wrong). After a successful opening of the region file, you save it as LYT file (otherwise you have very limited editing capabilities) and add the footprint to the library.
 
P.S. Under the hood the tool utilizes a proprietary temporary PCB design format, and original file parser is separated from target file writer. This is done just in case at some point in future I'll have to switch to some other EDA tools on either end. PADS parser was developed according to a publicly available format specification (though it's not very clear on certain subjects) and ARES writer was developed by trial and error.
