﻿/*
 * >>>> Describe the functionality of this file. <<<<
 *
 * Comment: >>>> Optional comment. <<<<
 *
 * Author:  >>>> Put your full name here <<<<
 *
 * Version: $Revision: 1.3 $ / $Date: 2008/11/27 11:47:02 $
 */

using System;
using System.Xml;
using System.Drawing;
using System.Windows.Forms;

namespace TestCollapsible
{
    /// <summary>
    /// Description of ShepherdTest.
    /// </summary>
    public partial class ShepherdTest : Form
    {
        public ShepherdTest()
        {
            //
            // The InitializeComponent() call is required for Windows Forms designer support.
            //
            InitializeComponent();
            
            //
            // TODO: Add constructor code after the InitializeComponent() call.
            //
        }
        
        public ShepherdTest(XmlNode AXmlNode)
        {
            //
            // The InitializeComponent() call is required for Windows Forms designer support.
            //
            InitializeComponent();
            
            tPnlCollapsible1.TaskListNode = AXmlNode;            
            tPnlCollapsible1.Collapse();
            tPnlCollapsible1.Expand();
        }
    }
}
