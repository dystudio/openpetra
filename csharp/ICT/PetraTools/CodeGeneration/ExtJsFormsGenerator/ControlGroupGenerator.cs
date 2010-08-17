﻿//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       timop
//
// Copyright 2004-2010 by OM International
//
// This file is part of OpenPetra.org.
//
// OpenPetra.org is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// OpenPetra.org is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with OpenPetra.org.  If not, see <http://www.gnu.org/licenses/>.
//
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Xml;
using Ict.Tools.CodeGeneration;
using Ict.Common.IO;
using Ict.Common;
using Ict.Tools.DBXML;

namespace Ict.Tools.CodeGeneration.ExtJs
{
    public class GroupBoxGenerator : TControlGenerator
    {
        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="type"></param>
        public GroupBoxGenerator(string prefix)
            : base(prefix, "none")
        {
            FGenerateLabel = false;

            if (base.FPrefix == "rng")
            {
                FGenerateLabel = true;
            }
        }

        /// <summary>
        /// this is currently only overloaded by RadioGroupGenerator
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="curNode"></param>
        /// <returns></returns>
        public virtual StringCollection FindContainedControls(TFormWriter writer, XmlNode curNode)
        {
            return new StringCollection();
        }

        public override ProcessTemplate SetControlProperties(TFormWriter writer, TControlDef ctrl)
        {
            ProcessTemplate snippetRowDefinition = writer.FTemplate.GetSnippet("RADIOGROUPDEFINITION");

            StringCollection Controls = FindContainedControls(writer, ctrl.xmlNode);

            foreach (string ChildControlName in Controls)
            {
                TControlDef childCtrl = FCodeStorage.FindOrCreateControl(ChildControlName, ctrl.controlName);
                IControlGenerator ctrlGen = writer.FindControlGenerator(childCtrl);
                ProcessTemplate ctrlSnippet = ctrlGen.SetControlProperties(writer, childCtrl);

                ctrlSnippet.SetCodelet("COLUMNWIDTH", "");

                ctrlSnippet.SetCodelet("ITEMNAME", ctrl.controlName);

                if (ChildControlName == Controls[0])
                {
                    ctrlSnippet.SetCodelet("LABEL", ctrl.Label);
                }

                snippetRowDefinition.InsertSnippet("ITEMS", ctrlSnippet, ",");
            }

            return snippetRowDefinition;
        }
    }
}