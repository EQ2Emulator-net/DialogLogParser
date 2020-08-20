using System;
using System.Collections.Generic;
using System.Windows.Forms;

using System.IO;
using System.Diagnostics;

namespace DialogLogParser
{
    public partial class DialogLogParser : Form
    {
        public DialogLogParser()
        {
            InitializeComponent();
        }

        Dictionary<string, NPC> NPCList = new Dictionary<string, NPC>();

        private void btnOpen_Click(object sender, EventArgs e)
        {
            // Current branch we are in so we know what to add options to
            Branch current_branch = null;
            NPC current_npc = null;
            OpenFileDialog fd = new OpenFileDialog();
            int index = 0;
            if (fd.ShowDialog() == DialogResult.OK)
            {
                txtLogPath.Text = fd.FileName;
                NPCList.Clear();
                lbNPCS.Items.Clear();
                lbNPCS.SelectedIndex = -1;

                StreamReader reader = new StreamReader(txtLogPath.Text);

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();

                    if (line.Contains("You say, \"Hail,"))
                    {
                        index = line.IndexOf("Hail,") + 6;
                        string npc_name = line.Substring(index, (line.Length - index) - 1);

                        if (NPCList.ContainsKey(npc_name))
                        {
                            current_npc = NPCList[npc_name];
                            current_branch = null;
                        }
                        else
                        {
                            NPC npc = new NPC();
                            npc.Name = npc_name;
                            NPCList.Add(npc_name, npc);
                            current_npc = npc;
                            current_branch = null;
                            lbNPCS.Items.Add(npc_name);
                        }
                    }

                    if (line.Contains("\\/a says to you"))
                    {
                        index = line.IndexOf(",") + 3; //line.IndexOf("says to you,") + 14;
                        string reply = line.Substring(index, (line.Length - index) - 1);

                        if (current_branch == null)
                        {
                            if (current_npc.Dialogs.Count > 0)
                            {
                                for (int i = 0; i < current_npc.Dialogs.Count; i++)
                                {
                                    if (current_npc.Dialogs[i].Text == reply)
                                    {
                                        current_branch = current_npc.Dialogs[i];
                                        break;
                                    }
                                }
                                if (current_branch == null)
                                {
                                    Branch branch = new Branch();
                                    current_npc.Dialogs.Add(branch);
                                    current_branch = branch;
                                    current_branch.Text = reply;
                                }

                            }
                            else
                            {
                                Branch branch = new Branch();
                                current_npc.Dialogs.Add(branch);
                                current_branch = branch;
                                current_branch.Text = reply;
                            }
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(current_branch.Text))
                            {
                                current_branch.Text = reply;
                            }
                        }
                    }

                    // Tells to the guild can interfere as well as talking to yourself, not sure how to handle talking to yourself yet
                    if (line.Contains("You say to ") && !line.Contains("You say to the guild"))
                    {
                        index = line.IndexOf(",") + 3;
                        string reply = line.Substring(index, (line.Length - index) - 1);
                        if (current_branch == null)
                        {
                            MessageBox.Show("Reply was orphaned:\n" + reply + "\n\nOriginal line from the log:\n" + line, "Error!", MessageBoxButtons.OK);
                        }
                        else
                        {
                            if (current_branch.Options.ContainsKey(reply))
                                current_branch = current_branch.Options[reply];
                            else
                            {
                                current_branch.Options.Add(reply, new Branch());
                                current_branch = current_branch.Options[reply];
                            }
                        }
                    }
                }

                reader.Close();
            }
        }

        private void btnParse_Click(object sender, EventArgs e)
        {
            if (lbNPCS.SelectedIndex < 0)
            {
                return;
            }

            NPC npc = NPCList[lbNPCS.SelectedItem.ToString()];
            npc.GenerateLua();
        }
    }

    public class Branch
    {
        // What the NPC says to you
        public string Text;
        // What you say to the NPC and the new Branch it leads to
        public Dictionary<string, Branch> Options;


        public UInt32 optionID;

        public Branch()
        {
            Options = new Dictionary<string, Branch>();
            optionID = 0;
        }
    }

    public class NPC
    {
        public string Name;
        public List<Branch> Dialogs;

        UInt32 option = 0;
        public NPC()
        {
            Dialogs = new List<Branch>();
        }

        public void GenerateLua()
        {
            string file = Name.Replace(" ", "") + ".lua";
            StreamWriter writer = new StreamWriter(Application.StartupPath + "/" + file);

            writer.WriteLine("function hailed(NPC, Spawn)");
            writer.WriteLine("\tFaceTarget(NPC, Spawn)");
            writer.WriteLine("\tlocal conversation = CreateConversation()");

            foreach(Branch b in Dialogs)
            {
                writer.WriteLine();
                foreach (KeyValuePair<string, Branch> kvp in b.Options)
                {
                    if (kvp.Value == null || string.IsNullOrEmpty(kvp.Value.Text))
                    {
                        writer.WriteLine("\tAddConversationOption(conversation, \"" + kvp.Key + "\")");
                    }
                    else
                    {
                        writer.WriteLine("\tAddConversationOption(conversation, \"" + kvp.Key + "\", \"Option" + (kvp.Value.optionID = ++option) + "\")");
                    }
                }

                writer.WriteLine("\tStartConversation(conversation, NPC, Spawn, \"" + b.Text +"\")");
            }

            writer.WriteLine("end");

            foreach(Branch b in Dialogs)
            {
                foreach(KeyValuePair<string, Branch> kvp in b.Options)
                {
                    if (!string.IsNullOrEmpty(kvp.Value.Text))
                    LuaAddFunctions(writer, kvp.Value);
                }
            }

            writer.Close();

            if (MessageBox.Show(file + "has been created, would you like to open it?", "Done!", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                ProcessStartInfo info = new ProcessStartInfo(file);
                Process.Start(info);
            }
        }

        private void LuaAddFunctions(StreamWriter writer, Branch branch)
        {
            if (branch.optionID == 0)
                return;

            writer.WriteLine();
            writer.WriteLine("function Option" + branch.optionID + "(NPC, Spawn)");
            writer.WriteLine("\tFaceTarget(NPC, Spawn)");
            writer.WriteLine("\tlocal conversation = CreateConversation()");
            writer.WriteLine();

            foreach (KeyValuePair<string, Branch> kvp in branch.Options)
            {
                writer.WriteLine("\tAddConversationOption(conversation, \"" + kvp.Key + "\"" + ((kvp.Value == null || string.IsNullOrEmpty(kvp.Value.Text)) ? ")" : (", \"Option" + (kvp.Value.optionID = ++option) + "\")")));
                kvp.Value.optionID = option;
            }

            writer.WriteLine("\tStartConversation(conversation, NPC, Spawn, \"" + branch.Text + "\")");
            writer.WriteLine("end");

            foreach (KeyValuePair<string, Branch> kvp in branch.Options)
            {
                if (!string.IsNullOrEmpty(kvp.Value.Text))
                    LuaAddFunctions(writer, kvp.Value);
            }
        }
    }
}
